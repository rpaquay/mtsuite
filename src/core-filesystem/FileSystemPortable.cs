// Copyright 2026 Renaud Paquay All Rights Reserved.
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
using System.IO;
using System.Collections.Generic;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

public class FileSystemPortable : IFileSystem {
    [DllImport("libSystem", EntryPoint = "clonefile", SetLastError = true)]
    private static extern int clonefile(string src, string dst, uint flags);

    private readonly IPool<List<FileSystemEntry>> _entryListPool = new ListPool<FileSystemEntry>();
    private readonly IPool<byte[]> _copyFileBufferPool = PoolFactory<byte[]>.Create(() => new byte[1024 * 1024]);

    public bool AllowCloning { get; set; } = true;

    private readonly EnumerationOptions _enumerationOptions = new EnumerationOptions {
      RecurseSubdirectories = false,
      AttributesToSkip = FileAttributes.None,
      IgnoreInaccessible = false,
      ReturnSpecialDirectories = false
    };

    public FileSystemEntry GetEntry(FullPath path) {
      if (!TryGetEntry(path, out var entry)) {
        throw new FileNotFoundException("Entry not found", path.FullName);
      }
      return entry;
    }

    public bool TryGetEntry(FullPath path, out FileSystemEntry entry) {
      var fullName = path.FullName;
      var fileInfo = new FileInfo(fullName);
      var attributes = fileInfo.Attributes;

      if ((int)attributes == -1) {
        // Double check LinkTarget for broken symlinks or DirectoryInfo
        if (fileInfo.LinkTarget != null) {
          attributes = FileAttributes.ReparsePoint;
        } else {
          entry = default;
          return false;
        }
      }

      var isDir = (attributes & FileAttributes.Directory) != 0;
      var isReparse = (attributes & FileAttributes.ReparsePoint) != 0 || fileInfo.LinkTarget != null;
      var length = (isDir || isReparse) ? 0 : (fileInfo.Exists ? fileInfo.Length : 0);

      long fileTimeUtc = 0;
      try {
        fileTimeUtc = fileInfo.LastWriteTimeUtc.ToFileTimeUtc();
      } catch {
        // Fallback for timestamps outside FILETIME range
      }

      var data = new FileSystemEntryData(attributes, length, fileTimeUtc);
      entry = new FileSystemEntry(path, data);
      return true;
    }

    public ReparsePointInfo GetReparsePointInfo(FullPath path) {
      var fullName = path.FullName;
      var info = new FileInfo(fullName);

      // On .NET 6+, LinkTarget retrieves the link target even for broken symlinks
      var target = info.LinkTarget ?? (Directory.Exists(fullName) ? new DirectoryInfo(fullName).LinkTarget : null);

      if (target == null && (info.Attributes & FileAttributes.ReparsePoint) == 0) {
        throw new FileNotFoundException("Entry not found or not a reparse point", fullName);
      }

      bool isSymLink = target != null;
      bool isJunction = OperatingSystem.IsWindows() && 
                        (info.Attributes & FileAttributes.Directory) != 0 && 
                        (target?.StartsWith(@"\??\") == true);

      return new ReparsePointInfo {
        IsJunctionPoint = isJunction,
        IsSymbolicLink = isSymLink,
        Target = target,
        IsTargetRelative = target != null && !Path.IsPathRooted(target),
        CreationTimeUtc = info.CreationTimeUtc,
        LastAccessTimeUtc = info.LastAccessTimeUtc,
        LastWriteTimeUtc = info.LastWriteTimeUtc,
      };
    }

    private sealed class DirectoryEntriesEnumerator : FileSystemEnumerator<FileSystemEntry> {
      private readonly FullPath _basePath;

      public DirectoryEntriesEnumerator(FullPath basePath, EnumerationOptions options)
        : base(basePath.FullName, options) {
        _basePath = basePath;
      }

      protected override bool ShouldIncludeEntry(ref System.IO.Enumeration.FileSystemEntry entry) {
        return true;
      }

      protected override bool ShouldRecurseIntoEntry(ref System.IO.Enumeration.FileSystemEntry entry) {
        return false;
      }

      protected override FileSystemEntry TransformEntry(ref System.IO.Enumeration.FileSystemEntry fsEntry) {
        var fileName = fsEntry.FileName.ToString();
        var entryPath = new FullPath(_basePath, fileName);
        var isDir = fsEntry.IsDirectory;
        var isReparse = (fsEntry.Attributes & FileAttributes.ReparsePoint) != 0;
        var length = (isDir || isReparse) ? 0 : fsEntry.Length;
        var data = new FileSystemEntryData(fsEntry.Attributes, length, fsEntry.LastWriteTimeUtc.UtcDateTime.ToFileTimeUtc());
        return new FileSystemEntry(entryPath, data);
      }
    }

    public FromPool<List<FileSystemEntry>> GetDirectoryFiles(FullPath path) {
      var list = _entryListPool.AllocateFrom();
      try {
        using var enumerator = new DirectoryEntriesEnumerator(path, _enumerationOptions);
        while (enumerator.MoveNext()) {
          list.Item.Add(enumerator.Current);
        }
      } catch {
        list.Dispose();
        throw;
      }

      return list;
    }

    public void CreateDirectory(FullPath path) {
      Directory.CreateDirectory(path.FullName);
    }

    public void DeleteEntry(FileSystemEntry entry) {
      RemoveAccessDeniedAttributes(entry);
      if (entry.IsDirectory) {
        Directory.Delete(entry.Path.FullName, recursive: false);
      } else {
        File.Delete(entry.Path.FullName);
      }
    }

    public void CopyFile<T>(FileSystemEntry sourceEntry, FileSystemEntry destinationEntry, CopyFileOptions options, T param, CopyFileCallback<T> callback) {
      CopyFileWorker(sourceEntry, destinationEntry.Path, destinationEntry, options, param, callback);
    }

    public void CopyFile<T>(FileSystemEntry sourceEntry, FullPath destinationPath, CopyFileOptions options, T param, CopyFileCallback<T> callback) {
      if (TryGetEntry(destinationPath, out var destinationEntry)) {
        CopyFileWorker(sourceEntry, destinationPath, destinationEntry, options, param, callback);
      } else {
        CopyFileWorker(sourceEntry, destinationPath, null, options, param, callback);
      }
    }

    private void CopyFileWorker<T>(FileSystemEntry sourceEntry, FullPath destinationPath, FileSystemEntry? destinationEntry, CopyFileOptions options, T param, CopyFileCallback<T> callback) {
      // If the source is a reparse point, delete the destination and copy the reparse point.
      if (sourceEntry.IsReparsePoint) {
        if (destinationEntry.HasValue) {
          try {
            DeleteEntry(destinationEntry.Value);
          } catch {
            // Nothing to do here, as CopyDirectoryReparsePoint will report an exception below.
          }
        }
        if (sourceEntry.IsDirectory) {
          CopyDirectoryReparsePoint(sourceEntry.Path, destinationPath);
        } else {
          CopyFileReparsePoint(sourceEntry.Path, destinationPath);
        }
      } else {
        // If destination exists and is read-only, remove the read-only attribute
        if (destinationEntry.HasValue) {
          try {
            RemoveAccessDeniedAttributes(destinationEntry.Value);
          } catch {
            // Nothing to do here, as CopyFile will report an exception below.
          }
        }
        
        if (AllowCloning && (options & CopyFileOptions.NoClone) == 0 && OperatingSystem.IsMacOS()) {
          if (TryCloneFile(sourceEntry, destinationPath, destinationEntry, options, param, callback)) {
            return;
          }
        }

        CopyFileImpl(sourceEntry, destinationPath, options, param, callback);
      }
    }

    private bool TryCloneFile<T>(
      FileSystemEntry sourceEntry,
      FullPath destinationPath,
      FileSystemEntry? destinationEntry,
      CopyFileOptions copyFileOptions,
      T param,
      CopyFileCallback<T> callback) {

      if (!OperatingSystem.IsMacOS()) {
        return false;
      }

      // If destination exists, clonefile fails with EEXIST unless deleted first
      if (destinationEntry.HasValue || File.Exists(destinationPath.FullName)) {
        try {
          File.Delete(destinationPath.FullName);
        } catch {
          // If destination cannot be deleted, fall back to streaming copy
          return false;
        }
      }

      int res = clonefile(sourceEntry.Path.FullName, destinationPath.FullName, 0);
      if (res != 0) {
        // clonefile failed (e.g. cross-volume copy or non-APFS volume), fall back to streaming copy
        return false;
      }

      // Preserve timestamps
      try {
        File.SetLastWriteTimeUtc(destinationPath.FullName, sourceEntry.LastWriteTimeUtc);
      } catch {
        // Best effort
      }

      // Preserve Unix file modes (POSIX permissions)
      try {
        var mode = File.GetUnixFileMode(sourceEntry.Path.FullName);
        File.SetUnixFileMode(destinationPath.FullName, mode);
      } catch {
        // Best effort
      }

      // Preserve FileAttributes
      try {
        if (sourceEntry.FileAttributes != FileAttributes.Normal) {
          File.SetAttributes(destinationPath.FullName, sourceEntry.FileAttributes);
        }
      } catch {
        // Best effort
      }

      // Notify callback that file copy is complete
      callback(ref sourceEntry, sourceEntry.FileSize, sourceEntry.FileSize, ref param);
      return true;
    }

    private void CopyFileImpl<T>(FileSystemEntry sourceEntry, FullPath destinationPath, CopyFileOptions copyFileOptions, T param, CopyFileCallback<T> callback) {
      using (var buffer = _copyFileBufferPool.AllocateFrom())
      using (var sourceStream = new FileStream(sourceEntry.Path.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 0, FileOptions.SequentialScan))
      using (var destinationStream = new FileStream(destinationPath.FullName, FileMode.Create, FileAccess.Write, FileShare.None, 0, FileOptions.SequentialScan)) {
        
        // Pre-allocate destination file size on SSD / filesystem
        if (sourceEntry.FileSize > 0) {
          try {
            destinationStream.SetLength(sourceEntry.FileSize);
          } catch {
            // Best effort for filesystems that do not support pre-allocation
          }
        }

        long totalBytesRead = 0;
        int bytesRead;
        while ((bytesRead = sourceStream.Read(buffer.Item, 0, buffer.Item.Length)) > 0) {
          destinationStream.Write(buffer.Item, 0, bytesRead);
          totalBytesRead += bytesRead;
          callback(ref sourceEntry, totalBytesRead, sourceEntry.FileSize, ref param);
        }

        // If file was empty, invoke callback at least once
        if (totalBytesRead == 0) {
          callback(ref sourceEntry, 0, 0, ref param);
        }

        // File may have changed size during copy, this is an error
        if (totalBytesRead != sourceEntry.FileSize) {
          throw new IOException($"Size of source file has changed during copy ({totalBytesRead} != {sourceEntry.FileSize})");
        }
      }

      // Preserve timestamps
      try {
        File.SetLastWriteTimeUtc(destinationPath.FullName, sourceEntry.LastWriteTimeUtc);
      } catch {
        // Best effort
      }

      // Preserve Unix file modes (POSIX permissions)
      if (!OperatingSystem.IsWindows()) {
        try {
          var mode = File.GetUnixFileMode(sourceEntry.Path.FullName);
          File.SetUnixFileMode(destinationPath.FullName, mode);
        } catch {
          // Best effort
        }
      }

      // Preserve FileAttributes (ReadOnly applied last so write streams aren't blocked)
      try {
        if (sourceEntry.FileAttributes != FileAttributes.Normal) {
          File.SetAttributes(destinationPath.FullName, sourceEntry.FileAttributes);
        }
      } catch {
        // Best effort
      }
    }

    public bool SupportsCloning(FullPath sourcePath, FullPath destinationPath) {
      if (!OperatingSystem.IsMacOS()) {
        return false;
      }

      try {
        if (!Directory.Exists(sourcePath.FullName) && !File.Exists(sourcePath.FullName)) {
          return false;
        }
        if (!Directory.Exists(destinationPath.FullName) && !File.Exists(destinationPath.FullName)) {
          return false;
        }

        string sourceDir = Directory.Exists(sourcePath.FullName) ? sourcePath.FullName : (Path.GetDirectoryName(sourcePath.FullName) ?? sourcePath.FullName);
        string destDir = Directory.Exists(destinationPath.FullName) ? destinationPath.FullName : (Path.GetDirectoryName(destinationPath.FullName) ?? destinationPath.FullName);

        string probeSrc = Path.Combine(sourceDir, ".mtcompact_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
        string probeDst = Path.Combine(destDir, ".mtcompact_probe_" + Guid.NewGuid().ToString("N") + ".tmp");

        try {
          File.WriteAllBytes(probeSrc, Array.Empty<byte>());
          int res = clonefile(probeSrc, probeDst, 0);
          return res == 0;
        } finally {
          try { if (File.Exists(probeSrc)) File.Delete(probeSrc); } catch { }
          try { if (File.Exists(probeDst)) File.Delete(probeDst); } catch { }
        }
      } catch {
        return false;
      }
    }

    public void CloneFile(FileSystemEntry sourceEntry, FullPath destinationPath) {
      if (!OperatingSystem.IsMacOS()) {
        throw new PlatformNotSupportedException("File cloning is currently only supported on macOS (APFS).");
      }

      string destDir = Path.GetDirectoryName(destinationPath.FullName) ?? destinationPath.FullName;
      string tempDst = Path.Combine(destDir, ".mtcompact_tmp_" + Guid.NewGuid().ToString("N") + ".tmp");

      int res = clonefile(sourceEntry.Path.FullName, tempDst, 0);
      if (res != 0) {
        int errno = Marshal.GetLastPInvokeError();
        try { if (File.Exists(tempDst)) File.Delete(tempDst); } catch { }
        throw new IOException($"Failed to clone file '{sourceEntry.Path}' to '{destinationPath}': errno {errno}");
      }

      // Preserve timestamps
      try {
        File.SetLastWriteTimeUtc(tempDst, sourceEntry.LastWriteTimeUtc);
      } catch { }

      // Preserve Unix file modes (POSIX permissions)
      try {
        var mode = File.GetUnixFileMode(sourceEntry.Path.FullName);
        File.SetUnixFileMode(tempDst, mode);
      } catch { }

      // Preserve FileAttributes
      try {
        if (sourceEntry.FileAttributes != FileAttributes.Normal) {
          File.SetAttributes(tempDst, sourceEntry.FileAttributes);
        }
      } catch { }

      // Atomic replace
      File.Move(tempDst, destinationPath.FullName, overwrite: true);
    }

    public FileStream OpenFile(FullPath path, FileAccess access) {
      return File.Open(path.FullName, FileMode.Open, access, FileShare.Read);
    }

    public FileStream CreateFile(FullPath path) {
      return new FileStream(path.FullName, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    public void CreateFileSymbolicLink(FullPath path, string target) {
      File.CreateSymbolicLink(path.FullName, target);
    }

    public void CreateDirectorySymbolicLink(FullPath path, string target) {
      Directory.CreateSymbolicLink(path.FullName, target);
    }

    public void CreateJunctionPoint(FullPath path, string target) {
      var targetPath = PathHelpers.IsPathAbsolute(target) ? target : path.Parent?.Combine(target).FullName;
      targetPath = PathHelpers.NormalizePath(targetPath);
      Directory.CreateSymbolicLink(path.FullName, targetPath);
    }
    
    private void CopyDirectoryReparsePoint(FullPath sourcePath, FullPath destinationPath) {
      var info = GetReparsePointInfo(sourcePath);

      if (info.IsSymbolicLink) {
        Directory.CreateSymbolicLink(destinationPath.FullName, info.Target);
        try {
          Directory.SetLastWriteTimeUtc(destinationPath.FullName, info.LastWriteTimeUtc);
        } catch {
          // Best effort
        }
      } else {
        throw new NotSupportedException(
          $"Error copying reparse point \"{sourcePath}\" (unsupported reparse point type?)");
      }
    }

    private void CopyFileReparsePoint(FullPath sourcePath, FullPath destinationPath) {
      var info = GetReparsePointInfo(sourcePath);

      if (info.IsSymbolicLink) {
        File.CreateSymbolicLink(destinationPath.FullName, info.Target);
        try {
          File.SetLastWriteTimeUtc(destinationPath.FullName, info.LastWriteTimeUtc);
        } catch {
          // Best effort
        }
      } else {
        throw new NotSupportedException(
          $"Error copying reparse point \"{sourcePath}\" (unsupported reparse point type?)");
      }
    }
    
    private void RemoveAccessDeniedAttributes(FileSystemEntry entry) {
      if (entry.IsReadOnly || entry.IsSystem) {
        try {
          var attrs = entry.FileAttributes & ~(FileAttributes.ReadOnly | FileAttributes.System);
          File.SetAttributes(entry.Path.FullName, attrs);
        } catch {
          // Best effort
        }
      }
    }
}
