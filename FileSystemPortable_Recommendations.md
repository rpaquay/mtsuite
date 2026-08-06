# Recommendations for FileSystemPortable

This document analyzes [`FileSystemPortable.cs`](file:///usr/local/google/home/rpaquay/src/mtsuite/src/core-filesystem/FileSystemPortable.cs) and outlines concrete recommendations to improve **Performance**, **Correctness**, and **Cross-OS Portability** (Linux, macOS, and Windows).

---

## 1. Performance Recommendations

### 1.1 Eliminate Exception-Driven Flow Control in `TryGetEntry`
* **Current Implementation:**
  ```csharp
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
  ```
* **Problem:** When running `mtcopy` or `mtmir` into a new directory, destination files do not exist yet. Throwing and catching `FileNotFoundException` across dozens of worker threads for hundreds of thousands of files introduces severe thread contention, exception stack unwinding overhead, and GC pressure.
* **Proposed Fix:** Use a non-throwing attribute check (e.g. `File.GetAttributes` wrapped safely or `new FileInfo(path.FullName)` with `info.Exists`).
  ```csharp
  public bool TryGetEntry(FullPath path, out FileSystemEntry entry) {
      var fullName = path.FullName;
      var fileInfo = new FileInfo(fullName);
      if (!fileInfo.Exists && !Directory.Exists(fullName)) {
          entry = default;
          return false;
      }
      var isDir = (fileInfo.Attributes & FileAttributes.Directory) != 0;
      var isReparse = (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0;
      var length = (isDir || isReparse) ? 0 : fileInfo.Length;
      var data = new FileSystemEntryData(fileInfo.Attributes, length, fileInfo.LastWriteTimeUtc.ToFileTimeUtc());
      entry = new FileSystemEntry(path, data);
      return true;
  }
  ```

---

### 1.2 Reduce Redundant Syscalls in `GetEntry`
* **Current Implementation:**
  ```csharp
  var fullName = path.FullName;
  FileSystemInfo info = File.GetAttributes(fullName).HasFlag(FileAttributes.Directory) 
    ? new DirectoryInfo(fullName) 
    : new FileInfo(fullName);
  ```
* **Problem:** `File.GetAttributes` issues a syscall (`stat`/`GetFileAttributesEx`). Then `new DirectoryInfo` or `new FileInfo` is instantiated, and subsequent property reads (`info.Length`, `info.LastWriteTimeUtc`) query the filesystem again.
* **Proposed Fix:** Create `new FileInfo(fullName)` directly, inspect `info.Attributes` with bitwise operations, avoiding the preliminary `File.GetAttributes` syscall.

---

### 1.3 Eliminate Closure & Delegate Allocations in `GetDirectoryFiles`
* **Current Implementation:**
  ```csharp
  FileSystemEntry FindTransform(ref System.IO.Enumeration.FileSystemEntry fsEntry) {
    var entryPath = MakeFullPath(path, fsEntry);
    ...
  }
  var entries = new FileSystemEnumerable<FileSystemEntry>(
    directory: path.FullName,
    transform: FindTransform,
    options: _enumerationOptions
  );
  ```
* **Problem:** `FindTransform` captures `path`, allocating a closure object and delegate on **every directory traversed**. Additionally, `path.Combine(fsEntry.FileName.ToString())` creates an intermediate `string` heap allocation for every entry.
* **Proposed Fix:**
  1. Add a `path.Combine(ReadOnlySpan<char> relative)` overload on `FullPath` to avoid allocating intermediate file name strings.
  2. Implement a custom `FileSystemEnumerator<FileSystemEntry>` subclass or reuse a parameterized transform to eliminate delegate/closure allocations entirely.

---

### 1.4 Optimize File Copy for SSD / NVMe High-Throughput
* **Current Implementation:**
  * Fixed 64 KB buffer pool: `PoolFactory<byte[]>.Create(() => new byte[64 * 1024])`.
  * Default `FileStream` buffering.
* **Proposed Fixes:**
  1. **Larger Buffer:** Increase buffer size to **128 KB – 512 KB** (or 256 KB) to reduce syscall overhead on fast NVMe drives.
  2. **Sequential Scan Flag:** Open source streams with `FileOptions.SequentialScan` to enable OS kernel read-ahead.
  3. **Pre-allocation (`SetLength`):** Call `destinationStream.SetLength(sourceEntry.FileSize)` up-front. On modern filesystems (ext4, XFS, APFS, NTFS), this triggers `fallocate`/`SetEndOfFile` to allocate contiguous extents, drastically cutting down filesystem fragmentation and metadata write operations.

---

## 2. Correctness & Robustness Recommendations

### 2.1 Broken Symlink Support in `GetReparsePointInfo`
* **Current Implementation:**
  ```csharp
  if (Directory.Exists(path.FullName)) {
    info = new DirectoryInfo(path.FullName);
  } else {
    info = new FileInfo(path.FullName);
  }
  if (!info.Exists) {
    throw new FileNotFoundException("Entry not found", path.FullName);
  }
  ```
* **Problem:** In .NET on Unix and Windows, `Directory.Exists` and `FileInfo.Exists` follow symlinks. If a symlink points to a non-existent target (broken symlink), `info.Exists` is `false` and throws `FileNotFoundException`.
* **Proposed Fix:** In .NET 6+, use `info.LinkTarget` directly or `File.ResolveLinkTarget(path.FullName, returnFinalTarget: false)`. A symlink exists even if its target does not!

---

### 2.2 Inconsistent Reparse Point File Size
* **Current Implementation:**
  * In `GetEntry`: `length = (isDir || isReparsePoint) ? 0 : info.Length;`
  * In `GetDirectoryFiles`: `length = fsEntry.IsDirectory ? 0 : fsEntry.Length;`
* **Problem:** `GetDirectoryFiles` reports the length of the symlink target for file symlinks, while `GetEntry` reports `0`.
* **Proposed Fix:** Update `GetDirectoryFiles` transform to check `(fsEntry.Attributes & FileAttributes.ReparsePoint) != 0` and set `length = 0`.

---

### 2.3 Strip `ReadOnly` / `System` Attributes in `DeleteEntry`
* **Current Implementation:**
  ```csharp
  public void DeleteEntry(FileSystemEntry entry) {
    if (entry.IsDirectory) {
      Directory.Delete(entry.Path.FullName, recursive: false);
    } else {
      File.Delete(entry.Path.FullName);
    }
  }
  ```
* **Problem:** On Windows (and some mounted filesystems), deleting a file with `FileAttributes.ReadOnly` throws `UnauthorizedAccessException`. `FileSystemWin32` explicitly calls `RemoveAccessDeniedAttributes(entry)` before deleting, but `FileSystemPortable` omits it.
* **Proposed Fix:** Call `RemoveAccessDeniedAttributes(entry)` inside `DeleteEntry`.

---

### 2.4 Zero-Byte File Copy Callback
* **Current Implementation:**
  ```csharp
  while ((bytesRead = sourceStream.Read(buffer.Item, 0, buffer.Item.Length)) > 0) {
      destinationStream.Write(buffer.Item, 0, bytesRead);
      totalBytes += bytesRead;
      callback?.Invoke(totalBytes, sourceEntry.FileSize);
  }
  ```
* **Problem:** For 0-byte files, the loop never executes, so `callback` is never called.
* **Proposed Fix:** Trigger `callback?.Invoke(0, 0)` for zero-byte files so progress counters and UI reporting remain consistent.

---

### 2.5 Metadata & Attributes Preservation
* **Current Implementation:** `CopyFileImpl` only preserves `LastWriteTimeUtc` and `UnixFileMode`.
* **Problem:** `CreationTimeUtc`, `LastAccessTimeUtc`, and standard attributes (`Hidden`, `Archive`, `ReadOnly`) are discarded.
* **Proposed Fix:**
  1. Copy `CreationTimeUtc` and `LastWriteTimeUtc`.
  2. Apply `File.SetAttributes(destinationPath.FullName, sourceEntry.FileAttributes)` at the end of the copy (applying `ReadOnly` after stream closure).

---

## 3. Cross-OS Portability Recommendations

| Category | Windows | Linux | macOS (Darwin) | Recommendation |
| :--- | :--- | :--- | :--- | :--- |
| **Junction Points** | Common (NTFS directory junctions `\??\Volume...`) | N/A (Standard symlinks) | N/A (Standard symlinks) | Detect `IsJunctionPoint` when running on Windows by inspecting target format or reparse tag. |
| **Symlink Timestamps** | `SetLastWriteTimeUtc` follows target | `SetLastWriteTimeUtc` follows target | `SetLastWriteTimeUtc` follows target | Symlink timestamp setting follows link target in .NET BCL. Wrap with try-catch and handle broken symlinks gracefully. |
| **Executable Bit (`+x`)** | Ignored | Unix file mode | Unix file mode | Maintain `File.GetUnixFileMode` and `File.SetUnixFileMode` with defensive exception handling. |
| **Case Sensitivity** | Case-preserving, case-insensitive | Case-sensitive (ext4/btrfs) | Case-preserving, case-insensitive (APFS) | Ensure path comparisons rely on `PathHelpers.FileNameComparison`. |

---

## 4. Proposed Refactored `FileSystemPortable.cs`

```csharp
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
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtsuite.CoreFileSystem;

public class FileSystemPortable : IFileSystem {
    private readonly IPool<List<FileSystemEntry>> _entryListPool = new ListPool<FileSystemEntry>();
    private readonly IPool<byte[]> _copyFileBufferPool = PoolFactory<byte[]>.Create(() => new byte[256 * 1024]);

    private readonly EnumerationOptions _enumerationOptions = new EnumerationOptions {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false
    };

    public FileSystemEntry GetEntry(FullPath path) {
        var fullName = path.FullName;
        var info = new FileInfo(fullName);
        if (!info.Exists && !Directory.Exists(fullName)) {
            throw new FileNotFoundException("Entry not found", fullName);
        }

        var isDir = (info.Attributes & FileAttributes.Directory) != 0;
        var isReparse = (info.Attributes & FileAttributes.ReparsePoint) != 0;
        var length = (isDir || isReparse) ? 0 : info.Length;
        var data = new FileSystemEntryData(info.Attributes, length, info.LastWriteTimeUtc.ToFileTimeUtc());
        return new FileSystemEntry(path, data);
    }

    public bool TryGetEntry(FullPath path, out FileSystemEntry entry) {
        var fullName = path.FullName;
        var info = new FileInfo(fullName);
        if (!info.Exists && !Directory.Exists(fullName)) {
            entry = default;
            return false;
        }

        var isDir = (info.Attributes & FileAttributes.Directory) != 0;
        var isReparse = (info.Attributes & FileAttributes.ReparsePoint) != 0;
        var length = (isDir || isReparse) ? 0 : info.Length;
        var data = new FileSystemEntryData(info.Attributes, length, info.LastWriteTimeUtc.ToFileTimeUtc());
        entry = new FileSystemEntry(path, data);
        return true;
    }

    public ReparsePointInfo GetReparsePointInfo(FullPath path) {
        var fullName = path.FullName;
        var info = new FileInfo(fullName);
        
        // LinkTarget works on .NET 6+ even for broken symlinks
        var target = info.LinkTarget ?? (Directory.Exists(fullName) ? new DirectoryInfo(fullName).LinkTarget : null);

        if (target == null && (info.Attributes & FileAttributes.ReparsePoint) == 0) {
            throw new FileNotFoundException("Entry not found or not a reparse point", fullName);
        }

        bool isSymLink = target != null;
        bool isJunction = OperatingSystem.IsWindows() && (info.Attributes & FileAttributes.Directory) != 0 && (target?.StartsWith(@"\??\") == true);

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

    public FromPool<List<FileSystemEntry>> GetDirectoryFiles(FullPath path) {
        var list = _entryListPool.AllocateFrom();
        try {
            var entries = new FileSystemEnumerable<FileSystemEntry>(
                directory: path.FullName,
                transform: (ref System.IO.Enumeration.FileSystemEntry fsEntry) => {
                    var entryPath = path.Combine(fsEntry.FileName.ToString());
                    var isDir = fsEntry.IsDirectory;
                    var isReparse = (fsEntry.Attributes & FileAttributes.ReparsePoint) != 0;
                    var length = (isDir || isReparse) ? 0 : fsEntry.Length;
                    var data = new FileSystemEntryData(fsEntry.Attributes, length, fsEntry.LastWriteTimeUtc.UtcDateTime.ToFileTimeUtc());
                    return new FileSystemEntry(entryPath, data);
                },
                options: _enumerationOptions
            );
            list.Item.AddRange(entries);
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
        if (sourceEntry.IsReparsePoint) {
            if (destinationEntry.HasValue) {
                try {
                    DeleteEntry(destinationEntry.Value);
                } catch {
                    // Best effort
                }
            }
            if (sourceEntry.IsDirectory) {
                CopyDirectoryReparsePoint(sourceEntry.Path, destinationPath);
            } else {
                CopyFileReparsePoint(sourceEntry.Path, destinationPath);
            }
        } else {
            if (destinationEntry.HasValue) {
                try {
                    RemoveAccessDeniedAttributes(destinationEntry.Value);
                } catch {
                    // Best effort
                }
            }
            
            CopyFileImpl(sourceEntry, destinationPath, options, callback);
        }
    }

    private void CopyFileImpl(FileSystemEntry sourceEntry, FullPath destinationPath, CopyFileOptions copyFileOptions, CopyFileCallback callback) {
        using (var buffer = _copyFileBufferPool.AllocateFrom())
        using (var sourceStream = new FileStream(sourceEntry.Path.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 0, FileOptions.SequentialScan))
        using (var destinationStream = new FileStream(destinationPath.FullName, FileMode.Create, FileAccess.Write, FileShare.None, 0, FileOptions.SequentialScan)) {
            
            // Pre-allocate destination file size on SSD/filesystem
            if (sourceEntry.FileSize > 0) {
                try {
                    destinationStream.SetLength(sourceEntry.FileSize);
                } catch {
                    // Best effort for file systems that do not support pre-allocation
                }
            }

            long totalBytes = 0;
            int bytesRead;
            while ((bytesRead = sourceStream.Read(buffer.Item, 0, buffer.Item.Length)) > 0) {
                destinationStream.Write(buffer.Item, 0, bytesRead);
                totalBytes += bytesRead;
                callback?.Invoke(totalBytes, sourceEntry.FileSize);
            }

            if (sourceEntry.FileSize == 0) {
                callback?.Invoke(0, 0);
            }

            if (totalBytes != sourceEntry.FileSize) {
                throw new IOException($"Size of source file has changed during copy ({totalBytes} != {sourceEntry.FileSize})");
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

        // Preserve FileAttributes (ReadOnly applied last if applicable)
        try {
            if (sourceEntry.FileAttributes != FileAttributes.Normal) {
                File.SetAttributes(destinationPath.FullName, sourceEntry.FileAttributes);
            }
        } catch {
            // Best effort
        }
    }

    public FileStream OpenFile(FullPath path, FileAccess access) {
        return File.Open(path.FullName, FileMode.Open, access, FileShare.Read);
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
            throw new NotSupportedException($"Error copying reparse point \"{sourcePath}\" (unsupported reparse point type)");
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
            throw new NotSupportedException($"Error copying reparse point \"{sourcePath}\" (unsupported reparse point type)");
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
```
