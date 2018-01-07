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

using Microsoft.Win32.SafeHandles;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace mtsuite.CoreFileSystem.Win32 {
  public class Win32<TPath> {
    private readonly IPool<List<DirectoryEntry>> _entryListPool = new ListPool<DirectoryEntry>();
    private readonly IPool<StringBuffer> _stringBufferPool = PoolFactory<StringBuffer>.Create(() => new StringBuffer(64), x => x.Clear());
    private readonly IPool<ByteBuffer> _byteBufferPool = PoolFactory<ByteBuffer>.Create(() => new ByteBuffer(256));
    private readonly IPathSerializer<TPath> _pathSerializer;

    public Win32(IPathSerializer<TPath> pathSerializer) {
      _pathSerializer = pathSerializer;
    }

    public IPathSerializer<TPath> PathSerializer {
      get { return _pathSerializer; }
    }

    private static string StripPath(string path) {
      if (!PathHelpers.IsPathAbsolute(path))
        return path;
      return PathHelpers.StripLongPathPrefix(path);
    }

    private string StripPath(TPath path) {
      var sb = new StringBuffer(_pathSerializer.GetLength(path) + 1);
      _pathSerializer.CopyTo(path, sb);
      return StripPath(sb.Text);
    }

    /// <summary>
    /// Note: For testability, this function should be called through <see cref="IFileSystem"/>.
    /// </summary>
    public FromPool<List<DirectoryEntry>> GetDirectoryEntries(TPath path, string pattern = null) {
      // Start enumerating files
      WIN32_FIND_DATA data;
      var findHandle = FindFirstFile(path, pattern, out data);
      if (findHandle == null) {
        return _entryListPool.AllocateFrom();
      }
      using (findHandle) {
        var result = _entryListPool.AllocateFrom();
        try {
          while (true) {
            // Add entry
            AddResult(ref data, result.Item);

            // Try to get next
            if (!NativeMethods.FindNextFile(findHandle, out data)) {
              var lastWin32Error = Marshal.GetLastWin32Error();
              if (lastWin32Error != (int)Win32Errors.ERROR_NO_MORE_FILES) {
                throw new LastWin32ErrorException(lastWin32Error,
                  string.Format("Error during enumeration of files at \"{0}\"",
                    StripPath(path)));
              }
              break;
            }
          }
        } catch {
          result.Dispose();
          throw;
        }
        return result;
      }
    }

    /// <summary>
    /// Note: For testability, this function should be called through <see cref="IFileSystem"/>.
    /// </summary>
    public IEnumerable<DirectoryEntry> EnumerateDirectoryEntries(TPath path, string pattern = null) {
      WIN32_FIND_DATA data;
      var findHandle = FindFirstFile(path, pattern, out data);
      if (findHandle == null) {
        yield break;
      }
      using (findHandle) {
        while (true) {
          // Entry found, return it
          var entry = new DirectoryEntry(data.cFileName, data);
          if (!SkipSpecialEntry(ref data)) yield return entry;

          // Try to find next
          if (!NativeMethods.FindNextFile(findHandle, out data)) {
            var lastWin32Error = Marshal.GetLastWin32Error();
            if (lastWin32Error != (int)Win32Errors.ERROR_NO_MORE_FILES) {
              throw new LastWin32ErrorException(lastWin32Error,
                string.Format("Error during enumeration of files at \"{0}\"",
                  StripPath(path)));
            }
            break;
          }
        }
      }
    }

    /// <summary>
    /// Note: For testability, this function should be called through <see cref="IFileSystem"/>.
    /// </summary>
    public IEnumerable<WIN32_FIND_DATA> EnumerateDirectoryEntriesData(TPath path, string pattern = null) {
      WIN32_FIND_DATA data;
      var findHandle = FindFirstFile(path, pattern, out data);
      if (findHandle == null) {
        yield break;
      }
      using (findHandle) {
        while (true) {
          // Entry found, return it
          if (!SkipSpecialEntry(ref data)) yield return data;

          // Try to find next
          if (!NativeMethods.FindNextFile(findHandle, out data)) {
            var lastWin32Error = Marshal.GetLastWin32Error();
            if (lastWin32Error != (int)Win32Errors.ERROR_NO_MORE_FILES) {
              throw new LastWin32ErrorException(lastWin32Error,
                string.Format("Error during enumeration of files at \"{0}\"",
                  StripPath(path)));
            }
            break;
          }
        }
      }
    }

    private SafeFindHandle FindFirstFile(TPath path, string pattern, out WIN32_FIND_DATA data) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(path, sb.Item);
        // Build search pattern (on the stack) as path + "\\*" + '\0'
        sb.Item.Append('\\');
        sb.Item.Append(pattern ?? @"*");

        // Start enumerating files
        var findHandle = NativeMethods.FindFirstFileEx(
          sb.Item.Data,
          NativeMethods.FINDEX_INFO_LEVELS.FindExInfoBasic,
          out data,
          NativeMethods.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
          IntPtr.Zero,
          NativeMethods.FINDEX_ADDITIONAL_FLAGS.FindFirstExLargeFetch);
        if (findHandle.IsInvalid) {
          var lastWin32Error = Marshal.GetLastWin32Error();
          if (lastWin32Error == (int)Win32Errors.ERROR_FILE_NOT_FOUND) {
            return null;
          }

          throw new LastWin32ErrorException(lastWin32Error,
            string.Format("Error enumerating files at \"{0}\"", StripPath(path)));
        }

        return findHandle;
      }
    }

    private static void AddResult(ref WIN32_FIND_DATA data, List<DirectoryEntry> entries) {
      var entry = new DirectoryEntry(data.cFileName, data);
      if (SkipSpecialEntry(ref data))
        return;

      entries.Add(entry);
    }

    private static bool SkipSpecialEntry(ref WIN32_FIND_DATA data) {
      return ((data.dwFileAttributes & (int)FILE_ATTRIBUTE.FILE_ATTRIBUTE_DIRECTORY) != 0) &&
              (".".Equals(data.cFileName) || "..".Equals(data.cFileName));
    }

    public void DeleteFile(TPath path) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(path, sb.Item);
        if (NativeMethods.DeleteFile(sb.Item.Data))
          return;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error deleting file \"{0}\"", StripPath(path)));
      }
    }

    public void DeleteDirectory(TPath path) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(path, sb.Item);
        // Note: RemoveDirectory works for empty directories as well
        // as for junction points, even if their target is not empty.
        if (NativeMethods.RemoveDirectory(sb.Item.Data))
          return;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error deleting directory \"{0}\"", StripPath(path)));
      }
    }

    public void SetFileAttributes(TPath path, FILE_ATTRIBUTE fileAttributes) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(path, sb.Item);
        if (NativeMethods.SetFileAttributes(sb.Item.Data, fileAttributes))
          return;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error setting file attributes on \"{0}\"", StripPath(path)));
      }
    }

    public FILE_ATTRIBUTE GetFileAttributes(TPath path) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(path, sb.Item);
        var INVALID_FILE_ATTRIBUTES = unchecked((FILE_ATTRIBUTE)(-1));
        var attributes = NativeMethods.GetFileAttributes(sb.Item.Data);
        if (attributes != INVALID_FILE_ATTRIBUTES)
          return attributes;

        var lastWin32Error = Marshal.GetLastWin32Error();
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error getting file attributes for \"{0}\"", StripPath(path)));
      }
    }

    public WIN32_FILE_ATTRIBUTE_DATA GetFileAttributesEx(TPath path) {
      WIN32_FILE_ATTRIBUTE_DATA data;
      var lastWin32Error = TryGetFileAttributesEx(path, out data);
      if (lastWin32Error == Win32Errors.ERROR_SUCCESS)
        return data;
      throw new LastWin32ErrorException((int)lastWin32Error,
        string.Format("Error getting extended file attributes for \"{0}\"", StripPath(path)));
    }

    public Win32Errors TryGetFileAttributesEx(TPath path, out WIN32_FILE_ATTRIBUTE_DATA data) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(path, sb.Item);
        if (NativeMethods.GetFileAttributesEx(sb.Item.Data, 0, out data))
          return Win32Errors.ERROR_SUCCESS;
        return (Win32Errors)Marshal.GetLastWin32Error();
      }
    }

    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    private static readonly NativeMethods.CopyProgressRoutine CopyProgressRoutine = CopyProgressFunction;
    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    private static readonly IntPtr CopyProgressRoutinePtr = Marshal.GetFunctionPointerForDelegate(CopyProgressRoutine);

    private static NativeMethods.CopyProgressResult CopyProgressFunction(
      long totalfilesize, long totalbytestransferred, long streamsize, long streambytestransferred, uint dwstreamnumber,
      NativeMethods.CopyProgressCallbackReason dwcallbackreason, IntPtr hsourcefile, IntPtr hdestinationfile, IntPtr lpdata) {

      var handle = GCHandle.FromIntPtr(lpdata);
      var data = (CopyFileCallbackData)handle.Target;
      try {
        data.Callback(totalbytestransferred, totalfilesize);
        return NativeMethods.CopyProgressResult.PROGRESS_CONTINUE;
      } catch (Exception e) {
        data.Error = e;
        return NativeMethods.CopyProgressResult.PROGRESS_CANCEL;
      }
    }

    private class CopyFileCallbackData {
      public CopyFileCallback Callback { get; set; }
      public Exception Error { get; set; }
    }

    public void CopyFile(TPath sourcePath, TPath destinationPath, CopyFileOptions options, CopyFileCallback callback) {
      using (var source = _stringBufferPool.AllocateFrom())
      using (var destination = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(sourcePath, source.Item);
        _pathSerializer.CopyTo(destinationPath, destination.Item);

        var callbackData = new CopyFileCallbackData { Callback = callback };
        var callbackDataHandle = GCHandle.Alloc(callbackData);
        try {
          var bCancel = 0;
          var flags = NativeMethods.CopyFileFlags.COPY_FILE_COPY_SYMLINK;
          if ((options & CopyFileOptions.Unbuffered) != 0)
            flags |= NativeMethods.CopyFileFlags.COPY_FILE_NO_BUFFERING;
          if (NativeMethods.CopyFileEx(source.Item.Data, destination.Item.Data,
                                       CopyProgressRoutinePtr, GCHandle.ToIntPtr(callbackDataHandle),
                                       ref bCancel, flags)) {
            return;
          }
        } finally {
          callbackDataHandle.Free();
        }

        var lastWin32Error = Marshal.GetLastWin32Error();
        if (lastWin32Error == (int)Win32Errors.ERROR_REQUEST_ABORTED && callbackData.Error != null) {
          if (callbackData.Error is OperationCanceledException) {
            throw callbackData.Error;
          }
          throw new InvalidOperationException(string.Format("Error copying file from \"{0}\" to \"{1}\"",
            StripPath(sourcePath), StripPath(destinationPath)), callbackData.Error);
        }
        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error copying file from \"{0}\" to \"{1}\"",
            StripPath(sourcePath), StripPath(destinationPath)));
      }
    }

    public void CreateDirectory(TPath path) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(path, sb.Item);

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

    public void CopyDirectoryReparsePoint(TPath sourcePath, TPath destinationPath) {
      var info = GetReparsePointInfo(sourcePath);

      if (info.IsSymbolicLink) {
        CreateDirectorySymbolicLink(destinationPath, info.Target);
        var fileTimes = new FileTimes {
          LastWriteTimeUtc = info.LastWriteTimeUtc,
        };
        using (var file = OpenFileAsReparsePoint(destinationPath, /*readWrite*/true)) {
          SetFileTimes(destinationPath, file, fileTimes);
        }
        return;
      }

      if (info.IsJunctionPoint) {
        CreateJunctionPoint(destinationPath, info.Target);
        var fileTimes = new FileTimes {
          LastWriteTimeUtc = info.LastWriteTimeUtc,
        };
        using (var file = OpenFileAsReparsePoint(destinationPath, /*readWrite*/true)) {
          SetFileTimes(destinationPath, file, fileTimes);
        }
        return;
      }

      throw new LastWin32ErrorException((int)Win32Errors.ERROR_NOT_SUPPORTED,
        string.Format("Error copying reparse point \"{0}\" (unsupported reparse point type?)", StripPath(sourcePath)));
    }

    public void CopyFileReparsePoint(TPath sourcePath, TPath destinationPath) {
      var info = GetReparsePointInfo(sourcePath);

      if (info.IsSymbolicLink) {
        CreateFileSymbolicLink(destinationPath, info.Target);
        var fileTimes = new FileTimes {
          LastWriteTimeUtc = info.LastWriteTimeUtc,
        };
        using (var file = OpenFileAsReparsePoint(destinationPath, /*readWrite*/true)) {
          SetFileTimes(destinationPath, file, fileTimes);
        }
        return;
      }

      throw new LastWin32ErrorException((int)Win32Errors.ERROR_NOT_SUPPORTED,
        string.Format("Error copying reparse point \"{0}\" (unsupported reparse point type?)", StripPath(sourcePath)));
    }

    public unsafe void SetFileTimes(TPath path, SafeFileHandle file, FileTimes fileTimes) {
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
      TPath path,
      NativeMethods.EFileAccess access,
      NativeMethods.EFileShare share,
      NativeMethods.ECreationDisposition creationDisposition,
      NativeMethods.EFileAttributes attributes) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(path, sb.Item);

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

    public SafeFileHandle OpenFileAsReparsePoint(TPath path, bool readWrite) {
      var access = NativeMethods.EFileAccess.FILE_GENERIC_READ;
      if (readWrite) {
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

    private static string ExtractPathFromSubstituteName(string substituteName) {
      if (string.IsNullOrEmpty(substituteName))
        return substituteName;

      var length = substituteName.Length;
      if (length < 6)
        return substituteName;

      if (substituteName[0] == '\\' &&
          substituteName[1] == '?' &&
          substituteName[2] == '?' &&
          substituteName[3] == '\\') {
        if (substituteName[length - 1] == '\\')
          length--;
        length -= 4;
        return substituteName.Substring(4, length);
      }

      return substituteName;
    }

    public void CreateDirectorySymbolicLink(TPath path, string targetPath) {
      CreateSymbolicLinkWorker(path, targetPath, NativeMethods.SYMBOLIC_LINK_FLAG.Directory);
    }

    public void CreateFileSymbolicLink(TPath path, string targetPath) {
      CreateSymbolicLinkWorker(path, targetPath, NativeMethods.SYMBOLIC_LINK_FLAG.File);
    }

    public void CreateSymbolicLinkWorker(
      TPath path,
      string targetPath,
      NativeMethods.SYMBOLIC_LINK_FLAG linkFlag) {
      using (var sb = _stringBufferPool.AllocateFrom()) {
        _pathSerializer.CopyTo(path, sb.Item);

        // Note: Win32 documentation for CreateSymbolicLink is incorrect (probably an issue in Windows).
        // On success, the function returns "1".
        // On error, the function returns some random value (e.g. 1280).
        // The best bet seems to use "GetLastError" and check for error/success.
        /*var statusCode = */
        NativeMethods.CreateSymbolicLink(sb.Item.Data, targetPath, linkFlag);
        var lastWin32Error = Marshal.GetLastWin32Error();
        if (lastWin32Error == (int)Win32Errors.ERROR_SUCCESS)
          return;

        throw new LastWin32ErrorException(lastWin32Error,
          string.Format("Error creating symbolic link \"{0}\" linking to \"{1}\"",
            StripPath(path), StripPath(targetPath)));
      }
    }

    /// <summary>
    /// Creates a (directory) junction point at <paramref name="path"/> with
    /// <paramref name="targetPath"/> as the target of the junction point.
    /// 
    /// Note that <paramref name="targetPath"/> must be an absolute path,
    /// but cannot start with a long path prefix "\\?\".
    /// 
    /// Note that <paramref name="path"/> must not exist on disk.
    /// </summary>
    public void CreateJunctionPoint(TPath path, string targetPath) {
      // http://www.flexhex.com/docs/articles/hard-links.phtml
      // To create a junction point, we need to:
      // 1. Create a regular directory entry
      // 2. Open a r/w handle to the directory entry
      // 3. Issue a FSCTL_SET_REPARSE_POINT I/O control to the directory handle

      // 1. Create directory
      CreateDirectory(path);

      // 2. Open directory as reparse point handle
      using (var fileHandle = OpenFileAsReparsePoint(path, /*readWrite*/true)) {

        // 3. Set reparse point to target
        using (var substituteNameBufferPooled = _stringBufferPool.AllocateFrom())
        using (var printNameBufferPooled = _stringBufferPool.AllocateFrom()) {
          // The "Substitute name" is the target path using the "\??\<path>\" format.
          var substituteNameBuffer = substituteNameBufferPooled.Item;

          var targetPathFixed = targetPath;
          targetPathFixed = PathHelpers.StripLongPathPrefix(targetPathFixed);

          substituteNameBuffer.Append(@"\??\");
          substituteNameBuffer.Append(targetPathFixed);
          substituteNameBuffer.Append('\\');

          // The "Print name" is the target path "as-is". Note that the print name
          // is optional, i.e. most applications seem to be able to deal with an empty
          // print name by falling back to the "substitute name". However, built-in
          // applications such as "mklink" seem to always set a "print name".
          var printNameBuffer = printNameBufferPooled.Item;
          printNameBuffer.Append(targetPath);

          // Fill up an instance of "MountPointReparseBuffer" that defines the reparse point target.
          SetMountPointReparse(fileHandle, path, substituteNameBuffer, printNameBuffer);
        }
      }
    }

    public unsafe ReparsePointInfo GetReparsePointInfo(TPath path) {
      using (var fileHandle = OpenFileAsReparsePoint(path, /*readWrite*/false)) {

        var creationTime = default(NativeMethods.FILETIME);
        var lastAccessTime = default(NativeMethods.FILETIME);
        var lastWriteTime = default(NativeMethods.FILETIME);
        if (!NativeMethods.GetFileTime(fileHandle, &creationTime, &lastAccessTime, &lastWriteTime)) {
          var lastWin32Error = Marshal.GetLastWin32Error();
          throw new LastWin32ErrorException(lastWin32Error,
            string.Format("Error getting file times of \"{0}\"", StripPath(path)));
        }

        using (var pooledBuffer = _byteBufferPool.AllocateFrom()) {
          var buffer = pooledBuffer.Item;

          // Keep trying until buffer is big enough
          while (true) {
            if (TryDeviceIoControl(path, fileHandle, NativeMethods.EIOControlCode.FsctlGetReparsePoint, buffer)) {
              break;
            }
            buffer.Capacity = buffer.Capacity * 2;
          }

          var header = new TypedBuffer<NativeMethods.REPARSE_DATA_BUFFER_HEADER>(buffer);
          var reparseTag = header.Read(x => x.ReparseTag);

          // IO_REPARSE_TAG_SYMLINK is the marker of a symbolic link
          if (reparseTag == NativeMethods.ReparseTagType.IO_REPARSE_TAG_SYMLINK) {
            var symlinkData = new TypedBuffer<NativeMethods.SymbolicLinkReparseData>(buffer);

            // Extract path from buffer
            var pathOffset = symlinkData.GetFieldOffset(x => x.PathBuffer);
            var substituteName = symlinkData.ReadString(
              pathOffset + symlinkData.Read(x => x.SubstituteNameOffset),
              symlinkData.Read(x => x.SubstituteNameLength) / sizeof(char));
            var printName = symlinkData.ReadString(
              pathOffset + symlinkData.Read(x => x.PrintNameOffset),
              symlinkData.Read(x => x.PrintNameLength) / sizeof(char));
            return new ReparsePointInfo {
              IsJunctionPoint = false,
              IsSymbolicLink = true,
              Target = string.IsNullOrEmpty(printName) ? substituteName : printName,
              IsTargetRelative = (symlinkData.Read(x => x.Flags) & NativeMethods.SymbolicLinkFlags.SYMLINK_FLAG_RELATIVE) != 0,
              CreationTimeUtc = creationTime.ToDateTimeUtc(),
              LastAccessTimeUtc = lastAccessTime.ToDateTimeUtc(),
              LastWriteTimeUtc = lastWriteTime.ToDateTimeUtc(),
            };
          }

          // IO_REPARSE_TAG_MOUNT_POINT is the marker of a Juntion Point
          if (reparseTag == NativeMethods.ReparseTagType.IO_REPARSE_TAG_MOUNT_POINT) {
            var mountPoint = new TypedBuffer<NativeMethods.MountPointReparseBuffer>(buffer);

            var pathOffset = mountPoint.GetFieldOffset(x => x.PathBuffer);
            var substituteName = mountPoint.ReadString(
              pathOffset + mountPoint.Read(x => x.SubstituteNameOffset),
              mountPoint.Read(x => x.SubstituteNameLength) / sizeof(char));
            var printName = mountPoint.ReadString(
              pathOffset + mountPoint.Read(x => x.PrintNameOffset),
              mountPoint.Read(x => x.PrintNameLength) / sizeof(char));
            return new ReparsePointInfo {
              IsJunctionPoint = true,
              IsSymbolicLink = false,
              Target = string.IsNullOrEmpty(printName) ? ExtractPathFromSubstituteName(substituteName) : printName,
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

    private bool TryDeviceIoControl(TPath path, SafeFileHandle fileHandle, NativeMethods.EIOControlCode ioControlCode, ByteBuffer buffer) {
      int bytesReturned;
      var success = NativeMethods.DeviceIoControl(
        fileHandle,
        ioControlCode,
        IntPtr.Zero,
        0,
        buffer.Pointer,
        buffer.Capacity,
        out bytesReturned,
        IntPtr.Zero);
      if (success)
        return true;

      var lastWin32Error = Marshal.GetLastWin32Error();
      if (lastWin32Error == (int)Win32Errors.ERROR_INSUFFICIENT_BUFFER || lastWin32Error == (int)Win32Errors.ERROR_MORE_DATA) {
        return false;
      }

      throw new LastWin32ErrorException(lastWin32Error,
        string.Format("Error reading target of reparse point \"{0}\"", StripPath(path)));
    }

    private void SetMountPointReparse(SafeFileHandle fileHandle, TPath path, StringBuffer substituteNameBuffer, StringBuffer printNameBuffer) {
      using (var pooledBuffer = _byteBufferPool.AllocateFrom()) {
        var buffer = pooledBuffer.Item;

        var header = new TypedBuffer<NativeMethods.REPARSE_DATA_BUFFER_HEADER>(buffer);
        var reparseBuffer = new TypedBuffer<NativeMethods.MountPointReparseBuffer>(buffer);
        // |ReparseDataLength| = the number of bytes after the |reparseBuffer.Header|.
        var reparseDataLength =
          reparseBuffer.GetFieldSize(x => x.SubstituteNameOffset) +
          reparseBuffer.GetFieldSize(x => x.SubstituteNameLength) +
          reparseBuffer.GetFieldSize(x => x.PrintNameOffset) +
          reparseBuffer.GetFieldSize(x => x.PrintNameLength) +
          (substituteNameBuffer.Length + 1) * sizeof(char) +
          (printNameBuffer.Length + 1) * sizeof(char);
        var bufferLength = header.SizeOf + reparseDataLength;

        header.Write(x => x.ReparseTag, (long)NativeMethods.ReparseTagType.IO_REPARSE_TAG_MOUNT_POINT);
        header.Write(x => x.ReparseDataLength, reparseDataLength);
        header.Write(x => x.Reserved, 0);

        // Offset/Length of substitute name
        // * both offset and count are counting *bytes*
        // * length does not include the terminatning NULL character
        reparseBuffer.Write(x => x.SubstituteNameOffset, 0);
        reparseBuffer.Write(x => x.SubstituteNameLength, substituteNameBuffer.Length * sizeof(char));

        // Offset/Length of print name
        // * both offset and count are counting *bytes*
        // * length does not include the terminatning NULL character
        reparseBuffer.Write(x => x.PrintNameOffset, (substituteNameBuffer.Length + 1) * sizeof(char));
        reparseBuffer.Write(x => x.PrintNameLength, printNameBuffer.Length * sizeof(char));

        // Copy the substitute name and print name sequentially into |PathBuffer|
        // Note: The "+1" is because we need to copy the terminating NULL character.
        var pathOffset = reparseBuffer.GetFieldOffset(x => x.PathBuffer);
        reparseBuffer.WriteString(
          pathOffset /* byte offset! */,
          substituteNameBuffer);
        reparseBuffer.WriteString(
          pathOffset + (substituteNameBuffer.Length + 1) * sizeof(char) /* byte offset! */,
          printNameBuffer);

        int bytesReturned;
        var success = NativeMethods.DeviceIoControl(
          fileHandle,
          NativeMethods.EIOControlCode.FsctlSetReparsePoint,
          buffer.Pointer,
          bufferLength,
          IntPtr.Zero,
          0,
          out bytesReturned,
          IntPtr.Zero);
        if (!success) {
          var lastWin32Error = Marshal.GetLastWin32Error();
          throw new LastWin32ErrorException(lastWin32Error,
            string.Format("Error creating junction point \"{0}\"", StripPath(path)));
        }
      }
    }
  }
}
