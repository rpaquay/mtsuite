// Copyright 2015 Renaud Paquay All Rights Reserved.
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
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtsuite.CoreFileSystem;

public class FileSystemPortable : IFileSystem {
    private readonly IPool<List<FileSystemEntry>> _entryListPool = new ListPool<FileSystemEntry>();
    private readonly IPool<byte[]> _copyFileBufferPool = PoolFactory<byte[]>.Create(() => new byte[64 * 1024]);

    private readonly EnumerationOptions _enumerationOptions = new EnumerationOptions() {
      RecurseSubdirectories = false,
      AttributesToSkip = FileAttributes.None
    };

    public FileSystemEntry GetEntry(FullPath path) {
      var fullName = path.FullName;
      FileSystemInfo info = File.GetAttributes(fullName).HasFlag(FileAttributes.Directory) 
        ? new DirectoryInfo(fullName) 
        : new FileInfo(fullName);
      var length =
        info.Attributes.HasFlag(FileAttributes.Directory) || info.Attributes.HasFlag(FileAttributes.ReparsePoint)
          ? 0
          : ((FileInfo)info).Length;
      var data = new FileSystemEntryData(info.Attributes, length, info.LastWriteTimeUtc.ToFileTimeUtc());
      return new FileSystemEntry(path, data);
    }

    public ReparsePointInfo GetReparsePointInfo(FullPath path) {
      // Note: This relies on .NET 6+ / .NET Standard 2.1+ APIs for LinkTarget
      FileSystemInfo info;
      if (Directory.Exists(path.FullName)) {
        info = new DirectoryInfo(path.FullName);
      } else {
        info = new FileInfo(path.FullName);
      }

      if (!info.Exists) {
         throw new FileNotFoundException("Entry not found", path.FullName);
      }

      // Basic support for symlinks if the runtime supports it
      var target = info.LinkTarget;
      bool isSymLink = (info.Attributes & FileAttributes.ReparsePoint) != 0 && target != null;

      return new ReparsePointInfo {
        IsJunctionPoint = false, // Hard to detect portably without specific platform checks, assuming false or implementation specific
        IsSymbolicLink = isSymLink,
        Target = target,
        IsTargetRelative = target != null && !Path.IsPathRooted(target),
        CreationTimeUtc = info.CreationTimeUtc,
        LastAccessTimeUtc = info.LastAccessTimeUtc,
        LastWriteTimeUtc = info.LastWriteTimeUtc,
      };
    }


    public FromPool<List<FileSystemEntry>> GetDirectoryFiles(FullPath path) {
      var list = _entryListPool.AllocateFrom();
      try {
        // Transform a `System.IO.Enumeration.FileSystemEntry` into our `FileSystemEntry`
        FileSystemEntry FindTransform(ref System.IO.Enumeration.FileSystemEntry fsEntry) {
          var entryPath = MakeFullPath(path, fsEntry);
          var length = fsEntry.IsDirectory ? 0 : fsEntry.Length;
          var data = MakeFileSystemEntryData(fsEntry, length);
          var entry = new FileSystemEntry(entryPath, data);
          return entry;
        }

        var entries = new FileSystemEnumerable<FileSystemEntry>(
          directory: path.FullName,
          transform: FindTransform,
          options: _enumerationOptions
        );
        list.Item.AddRange(entries);
      } catch {
        list.Dispose();
        throw;
      }

      return list;
    }

    private static FileSystemEntryData MakeFileSystemEntryData(System.IO.Enumeration.FileSystemEntry fsEntry, long length) {
        return new FileSystemEntryData(fsEntry.Attributes, length, fsEntry.LastWriteTimeUtc.UtcDateTime.ToFileTimeUtc());
    }

    private static FullPath MakeFullPath(FullPath path, System.IO.Enumeration.FileSystemEntry fsEntry) {
        return path.Combine(fsEntry.FileName.ToString());
    }

    public void CreateDirectory(FullPath path) {
      Directory.CreateDirectory(path.FullName);
    }

    public void DeleteEntry(FileSystemEntry entry) {
      if (entry.IsDirectory) {
        Directory.Delete(entry.Path.FullName, recursive: false);
      } else {
        File.Delete(entry.Path.FullName);
      }
    }

    public void CopyFile(FileSystemEntry sourceEntry, FileSystemEntry destinationEntry, CopyFileOptions options, CopyFileCallback callback) {
        CopyFileWorker(sourceEntry, destinationEntry.Path, destinationEntry, options, callback);
    }

    public void CopyFile(FileSystemEntry sourceEntry, FullPath destinationPath, CopyFileOptions options, CopyFileCallback callback) {
        if (TryGetEntry(destinationPath, out var destinationEntry)) {
            CopyFileWorker(sourceEntry, destinationPath, destinationEntry, options, callback);
        } else {
            CopyFileWorker(sourceEntry, destinationPath, null, options, callback);
        }
    }

    private void CopyFileWorker(FileSystemEntry sourceEntry, FullPath destinationPath, FileSystemEntry? destinationEntry, CopyFileOptions options, CopyFileCallback callback) {
        // If the source is a reparse point, delete the destination and
        // copy the reparse point.
        if (sourceEntry.IsReparsePoint) {
            if (destinationEntry.HasValue) {
                try {
                    DeleteEntry(destinationEntry.Value);
                } catch {
                    // Nothing to do here, as CopyDirectoryReparsePoint will report an exception below.
                }
            }
            if (sourceEntry.IsDirectory) {
                //File.CreateSymbolicLink(destinationPath.FullName, sourceEntry.);
                CopyDirectoryReparsePoint(sourceEntry.Path, destinationPath);
            } else {
                //Directory.CreateSymbolicLink()
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
            
            CopyFileImpl(sourceEntry, destinationPath, options, callback);
        }
    }

    private void CopyFileImpl(FileSystemEntry sourceEntry, FullPath destinationPath, CopyFileOptions copyFileOptions, CopyFileCallback callback) {
        using (var buffer = _copyFileBufferPool.AllocateFrom())
        using (var sourceStream = new FileStream(sourceEntry.Path.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var destinationStream = new FileStream(destinationPath.FullName, FileMode.Create, FileAccess.Write, FileShare.None)) {
            long totalBytes = 0;
            int bytesRead;
            while ((bytesRead = sourceStream.Read(buffer.Item, 0, buffer.Item.Length)) > 0) {
                destinationStream.Write(buffer.Item, 0, bytesRead);
                totalBytes += bytesRead;
                callback?.Invoke(totalBytes, sourceEntry.FileSize);
            }

            if (totalBytes != sourceEntry.FileSize) {
                throw new IOException($"Size of source file has changed during copy ({totalBytes} != {sourceEntry.FileSize})");
            }
        }

        try {
            File.SetLastWriteTimeUtc(destinationPath.FullName, sourceEntry.LastWriteTimeUtc);
        } catch {
            // Best effort
        }

        if (!OperatingSystem.IsWindows()) {
            try {
                var mode = File.GetUnixFileMode(sourceEntry.Path.FullName);
                File.SetUnixFileMode(destinationPath.FullName, mode);
            } catch {
                // Best effort
            }
        }
    }

    public FileStream OpenFile(FullPath path, FileAccess access) {
      return File.Open(path.FullName, FileMode.Open, access, FileShare.Read);
    }
    
    private bool TryGetEntry(FullPath path, out FileSystemEntry entry) {
        try {
            entry = GetEntry(path);
            return true;
        }
        catch {
            entry = default(FileSystemEntry);
            return false;
        }
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
        }
        else {
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
        }
        else {
            throw new NotSupportedException(
                $"Error copying reparse point \"{sourcePath}\" (unsupported reparse point type?)");
        }
    }
    
    private void RemoveAccessDeniedAttributes(FileSystemEntry entry) {
        if (entry.IsReadOnly || entry.IsSystem) {
            var attrs = entry.FileAttributes & ~(FileAttributes.ReadOnly | FileAttributes.System);
            File.SetAttributes(entry.Path.FullName, attrs);
        }
    }
}
