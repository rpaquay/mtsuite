﻿// Copyright 2015 Renaud Paquay All Rights Reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using mtsuite.shared.Collections;
using mtsuite.shared.Files;
using Microsoft.Win32.SafeHandles;

namespace mtsuite.shared.Win32 {
  public class Win32 {
    private readonly IPool<List<DirectoryEntry>> _entryListPool = new ListPool<DirectoryEntry>();
    private readonly IPool<StringBuffer> _stringBufferPool =
      PoolFactory<StringBuffer>.Create(
        () => new StringBuffer(64),
        x => x.Clear());

    private static string StripPath(string path) {
      if (!PathHelpers.IsPathAbsolute(path))
        return path;
      return PathHelpers.StripLongPathPrefix(path);
    }

    private static string StripPath(IStringSource path) {
      var sb = new StringBuffer(path.Length + 1);
      path.CopyTo(sb);
      return StripPath(sb.Text);
    }

    /// <summary>
    /// Note: For testability, this function should be called through <see cref="IFileSystem"/>.
    /// </summary>
    public FromPool<List<DirectoryEntry>> GetDirectoryEntries(IStringSource path) {
      // Build search pattern (on the stack) as path + "\\*" + '\0'
      using (var sb = _stringBufferPool.AllocateFrom()) {
        path.CopyTo(sb.Item);
        sb.Item.Append(@"\*");

        // Start enumerating files
        WIN32_FIND_DATA data;
        var findHandle = NativeMethods.FindFirstFileEx(
          sb.Item.Data,
          NativeMethods.FINDEX_INFO_LEVELS.FindExInfoBasic,
          out data,
          NativeMethods.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
          IntPtr.Zero,
          NativeMethods.FINDEX_ADDITIONAL_FLAGS.FindFirstExLargeFetch);
        if (findHandle.IsInvalid) {
          var lastWin32Error = Marshal.GetLastWin32Error();
          throw new LastWin32ErrorException(lastWin32Error,
            string.Format("Error enumerating files at \"{0}\"", StripPath(path)));
        }

        using (findHandle) {
          var result = _entryListPool.AllocateFrom();
          try {
            AddResult(ref data, result.Item);
            while (NativeMethods.FindNextFile(findHandle, out data)) {
              AddResult(ref data, result.Item);
            }
            var lastWin32Error = Marshal.GetLastWin32Error();
            if (lastWin32Error != (int) Win32Errors.ERROR_NO_MORE_FILES) {
              throw new LastWin32ErrorException(lastWin32Error,
                string.Format("Error during enumeration of files at \"{0}\"",
                  StripPath(path)));
            }
          } catch {
            result.Dispose();
            throw;
          }
          return result;
        }
      }
    }

    private static void AddResult(ref WIN32_FIND_DATA data, List<DirectoryEntry> entries) {
      var entry = new DirectoryEntry(data.cFileName, data);
      if (SkipSpecialEntry(entry))
        return;

      entries.Add(entry);
    }

    private static bool SkipSpecialEntry(DirectoryEntry entry) {
      return (entry.IsDirectory) && (entry.Name.Equals(".") || entry.Name.Equals(".."));
    }

    public void DeleteFile(IStringSource path) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        path.CopyTo(sb.Item);
        if (NativeMethods.DeleteFile(sb.Item.Data))
          return;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error deleting file \"{0}\"", StripPath(path)));
      }
    }

    public void DeleteDirectory(IStringSource path) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        path.CopyTo(sb.Item);
        if (NativeMethods.RemoveDirectory(sb.Item.Data))
          return;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error deleting directory \"{0}\"", StripPath(path)));
      }
    }

    public void SetFileAttributes(IStringSource path, FILE_ATTRIBUTE fileAttributes) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        path.CopyTo(sb.Item);
        if (NativeMethods.SetFileAttributes(sb.Item.Data, fileAttributes))
          return;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error setting file attributes on \"{0}\"", StripPath(path)));
      }
    }

    public FILE_ATTRIBUTE GetFileAttributes(IStringSource path) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        path.CopyTo(sb.Item);
        var INVALID_FILE_ATTRIBUTES = unchecked((FILE_ATTRIBUTE) (-1));
        var attributes = NativeMethods.GetFileAttributes(sb.Item.Data);
        if (attributes != INVALID_FILE_ATTRIBUTES)
          return attributes;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error getting file attributes for \"{0}\"", StripPath(path)));
      }
    }

    public WIN32_FILE_ATTRIBUTE_DATA GetFileAttributesEx(IStringSource path) {
      WIN32_FILE_ATTRIBUTE_DATA data;
      var lastWin32Error = TryGetFileAttributesEx(path, out data);
      if (lastWin32Error == Win32Errors.ERROR_SUCCESS)
        return data;
      throw new LastWin32ErrorException((int) lastWin32Error,
        string.Format("Error getting extended file attributes for \"{0}\"", StripPath(path)));
    }

    public Win32Errors TryGetFileAttributesEx(IStringSource path, out WIN32_FILE_ATTRIBUTE_DATA data) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        path.CopyTo(sb.Item);
        if (NativeMethods.GetFileAttributesEx(sb.Item.Data, 0, out data))
          return Win32Errors.ERROR_SUCCESS;
        return (Win32Errors) Marshal.GetLastWin32Error();
      }
    }

    public void CopyFile(IStringSource sourcePath, IStringSource destinationPath, CopyFileCallback callback) {
      // Note: object lifetime: CopyFileEx terminates within this function call, so
      // is it ok to have [callback] be a local variable.
      NativeMethods.CopyProgressRoutine copyProgress =
        (size, transferred, streamSize, bytesTransferred, number, reason, file, destinationFile, data) => {
          CopyFileCallback dataCallback =
            (CopyFileCallback) Marshal.GetDelegateForFunctionPointer(data, typeof (CopyFileCallback));
          dataCallback(transferred, size);
          return NativeMethods.CopyProgressResult.PROGRESS_CONTINUE;
        };

      using (var source = _stringBufferPool.AllocateFrom())
      using (var destination = _stringBufferPool.AllocateFrom()) {
        sourcePath.CopyTo(source.Item);
        destinationPath.CopyTo(destination.Item);

        var callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);
        var bCancel = 0;
        var flags = NativeMethods.CopyFileFlags.COPY_FILE_COPY_SYMLINK;
        if (NativeMethods.CopyFileEx(source.Item.Data, destination.Item.Data, copyProgress, callbackPtr, ref bCancel,
          flags))
          return;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error copying file from \"{0}\" to \"{1}\"",
            StripPath(sourcePath), StripPath(destinationPath)));
      }
    }

    public void CreateDirectory(IStringSource path) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        path.CopyTo(sb.Item);

        if (NativeMethods.CreateDirectory(sb.Item.Data, IntPtr.Zero))
          return;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error creating directory \"{0}\"", StripPath(path)));
      }
    }

    public class FileTimes {
      public DateTime? CreationTimeUtc { get; set; }
      public DateTime? LastAccessTimeUtc { get; set; }
      public DateTime? LastWriteTimeUtc { get; set; }
    }

    public void CopyDirectoryReparsePoint(IStringSource sourcePath, IStringSource destinationPath) {
      var info = GetReparsePointInfo(sourcePath);

      if (info.IsSymbolicLink) {
        CreateDirectorySymbolicLink(destinationPath, info.Target);
        var fileTimes = new FileTimes {
          LastWriteTimeUtc = info.LastWriteTimeUtc,
        };
        using (var file = OpenFileAsReparsePoint(destinationPath, false /*readonly*/)) {
          SetFileTimes(destinationPath, file, fileTimes);
        }
        return;
      }

      if (info.IsJunctionPoint) {
        CreateJunctionPoint(destinationPath, info.Target);
        return;
      }

      throw new InvalidOperationException("Unknown reparse point type");
    }

    public unsafe void SetFileTimes(IStringSource path, SafeFileHandle file, FileTimes fileTimes) {
      NativeMethods.FILETIME* lpCreationTime = null;
      NativeMethods.FILETIME* lpLastAccessTime = null;
      NativeMethods.FILETIME* lpLastWriteTime = null;
      NativeMethods.FILETIME creationTime;
      NativeMethods.FILETIME lastAccessTime;
      NativeMethods.FILETIME lastWriteTime;
      if (fileTimes.CreationTimeUtc != null) {
        creationTime = NativeMethods.FILETIME.FromDateTime(fileTimes.CreationTimeUtc.Value);
        lpCreationTime = &creationTime;
      }
      if (fileTimes.LastAccessTimeUtc != null) {
        lastAccessTime = NativeMethods.FILETIME.FromDateTime(fileTimes.LastAccessTimeUtc.Value);
        lpLastAccessTime = &lastAccessTime;
      }
      if (fileTimes.LastWriteTimeUtc != null) {
        lastWriteTime = NativeMethods.FILETIME.FromDateTime(fileTimes.LastWriteTimeUtc.Value);
        lpLastWriteTime = &lastWriteTime;
      }
      if (NativeMethods.SetFileTime(file, lpCreationTime, lpLastAccessTime, lpLastWriteTime))
        return;

      var lastWin32Error = Marshal.GetLastWin32Error();
      throw new LastWin32ErrorException(lastWin32Error,
        string.Format("Error setting file times of \"{0}\"", StripPath(path)));
    }

    public SafeFileHandle OpenFile(
      IStringSource path,
      NativeMethods.EFileAccess access,
      NativeMethods.EFileShare share,
      NativeMethods.ECreationDisposition creationDisposition,
      NativeMethods.EFileAttributes attributes) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        path.CopyTo(sb.Item);

        var fileHandle = NativeMethods.CreateFile(
          sb.Item.Data,
          access,
          share,
          IntPtr.Zero,
          creationDisposition,
          attributes,
          IntPtr.Zero);
        if (fileHandle.IsInvalid) {
          var lastWin32Error = Marshal.GetLastWin32Error();
          throw new LastWin32ErrorException(lastWin32Error,
            string.Format("Error opening file or directory \"{0}\"", StripPath(path)));
        }

        return fileHandle;
      }
    }

    public SafeFileHandle OpenFileAsReparsePoint(IStringSource path, bool readOnly) {
      var access = NativeMethods.EFileAccess.FILE_GENERIC_READ;
      if (!readOnly) {
        access |= NativeMethods.EFileAccess.FILE_GENERIC_WRITE;
      }
      return OpenFile(path, access,
        NativeMethods.EFileShare.Read | NativeMethods.EFileShare.Write | NativeMethods.EFileShare.Delete,
        NativeMethods.ECreationDisposition.OpenExisting,
        NativeMethods.EFileAttributes.BackupSemantics | NativeMethods.EFileAttributes.OpenReparsePoint);
    }

    public class ReparsePointInfo {
      public bool IsJunctionPoint { get; set; }
      public bool IsSymbolicLink { get; set; }
      public string Target { get; set; }
      public bool IsTargetRelative { get; set; }
      public DateTime CreationTimeUtc { get; set; }
      public DateTime LastAccessTimeUtc { get; set; }
      public DateTime LastWriteTimeUtc { get; set; }
    }

    public unsafe ReparsePointInfo GetReparsePointInfo(IStringSource path) {
      using (var fileHandle = OpenFileAsReparsePoint(path, true /*readonly*/)) {

        var creationTime = default(NativeMethods.FILETIME);
        var lastAccessTime = default(NativeMethods.FILETIME);
        var lastWriteTime = default(NativeMethods.FILETIME);
        if (!NativeMethods.GetFileTime(fileHandle, &creationTime, &lastAccessTime, &lastWriteTime)) {
          var lastWin32Error = Marshal.GetLastWin32Error();
          throw new LastWin32ErrorException(lastWin32Error,
            string.Format("Error getting file times of \"{0}\"", StripPath(path)));
        }

        var outBufferSize = Marshal.SizeOf(typeof (NativeMethods.REPARSE_DATA_BUFFER));
        using (var outBuffer = new SafeHGlobalHandle(Marshal.AllocHGlobal(outBufferSize))) {
          int bytesReturned;
          var success = NativeMethods.DeviceIoControl(
            fileHandle,
            NativeMethods.EIOControlCode.FsctlGetReparsePoint,
            IntPtr.Zero,
            0,
            outBuffer.Pointer,
            outBufferSize,
            out bytesReturned,
            IntPtr.Zero);
          if (!success) {
            var lastWin32Error = Marshal.GetLastWin32Error();
            throw new LastWin32ErrorException(lastWin32Error,
              string.Format("Error reading target of reparse point \"{0}\"", StripPath(path)));
          }

          var dataBuffer = outBuffer.ToStructure<NativeMethods.REPARSE_DATA_BUFFER>();
          if (dataBuffer.ReparseTag == NativeMethods.ReparseTagType.IO_REPARSE_TAG_SYMLINK) {
            // Extract path from buffer
            var reparseDataBuffer = outBuffer.ToStructure<NativeMethods.SymbolicLinkReparseData>();
            return new ReparsePointInfo {
              IsJunctionPoint = false,
              IsSymbolicLink = true,
              Target = Encoding.Unicode.GetString(
                reparseDataBuffer.PathBuffer, reparseDataBuffer.PrintNameOffset,
                reparseDataBuffer.PrintNameLength),
              IsTargetRelative = (reparseDataBuffer.Flags & NativeMethods.SymbolicLinkFlags.SYMLINK_FLAG_RELATIVE) != 0,
              CreationTimeUtc = creationTime.ToDateTimeUtc(),
              LastAccessTimeUtc = lastAccessTime.ToDateTimeUtc(),
              LastWriteTimeUtc = lastWriteTime.ToDateTimeUtc(),
            };
          }
          if (dataBuffer.ReparseTag == NativeMethods.ReparseTagType.IO_REPARSE_TAG_MOUNT_POINT) {
            // Extract path from buffer
            return new ReparsePointInfo {
              IsJunctionPoint = true,
              IsSymbolicLink = false,
              Target = Encoding.Unicode.GetString(
                dataBuffer.PathBuffer, dataBuffer.PrintNameOffset,
                dataBuffer.PrintNameLength),
              IsTargetRelative = false,
              CreationTimeUtc = creationTime.ToDateTimeUtc(),
              LastAccessTimeUtc = lastAccessTime.ToDateTimeUtc(),
              LastWriteTimeUtc = lastWriteTime.ToDateTimeUtc(),
            };
          }

          throw new NotSupportedException(
            string.Format("Unsupported reparse point type at \"{0}\"", StripPath(path)));
        }
      }
    }

    public void CreateDirectorySymbolicLink(IStringSource path, string targetPath) {
      CreateSymbolicLinkWorker(path, targetPath, NativeMethods.SYMBOLIC_LINK_FLAG.Directory);
    }

    public void CreateFileSymbolicLink(IStringSource path, string targetPath) {
      CreateSymbolicLinkWorker(path, targetPath, NativeMethods.SYMBOLIC_LINK_FLAG.File);
    }

    public void CreateSymbolicLinkWorker(
      IStringSource path,
      string targetPath,
      NativeMethods.SYMBOLIC_LINK_FLAG linkFlag) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        path.CopyTo(sb.Item);

        // Note: Win32 documentation for CreateSymbolicLink is incorrect (probably an issue in Windows).
        // On success, the function returns "1".
        // On error, the function returns some random value (e.g. 1280).
        // The best bet seems to use "GetLastError" and check for error/success.
        /*var statusCode = */
        NativeMethods.CreateSymbolicLink(sb.Item.Data, targetPath, linkFlag);
        var lastWin32Error = Marshal.GetLastWin32Error();
        if (lastWin32Error == (int) Win32Errors.ERROR_SUCCESS)
          return;

        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error creating symbolic link \"{0}\" linking to \"{1}\"",
            StripPath(path), StripPath(targetPath)));
      }
    }

    public void CreateJunctionPoint(IStringSource path, string targetPath) {
      var lastWin32Error = Win32Errors.ERROR_NOT_SUPPORTED;
      throw new LastWin32ErrorException((int) lastWin32Error,
        string.Format("Error creating junction point \"{0}\" linking to \"{1}\"",
          StripPath(path), StripPath(targetPath)));
    }
  }
}
