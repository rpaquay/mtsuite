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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// Helper class for POSIX-compliant filesystem operations (macOS, Linux),
/// providing common POSIX routines like directory entry deletion via <c>unlinkat</c>.
/// </summary>
/// <summary>
/// Helper class for POSIX-compliant filesystem operations (macOS, Linux),
/// providing common POSIX routines like directory entry deletion via <c>unlinkat</c>.
/// </summary>
public sealed class PosixFileSystemExtension {
  [DllImport("libc", EntryPoint = "open", SetLastError = true)]
  private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int oflag);

  [DllImport("libc", EntryPoint = "close", SetLastError = true)]
  private static extern int close(int fd);

  [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
  private static extern int unlinkat(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flag);

  private const int ENOENT = 2;

  private readonly IPool<StringBuffer> _fullNameBufferPool;

  public PosixFileSystemExtension(IPool<StringBuffer> fullNameBufferPool) {
    ArgumentNullException.ThrowIfNull(fullNameBufferPool);
    _fullNameBufferPool = fullNameBufferPool;
  }

  public bool DeleteDirectoryEntries<TState>(
    FileSystemEntry directory,
    IReadOnlyList<FileSystemEntry> entries,
    int openDirectoryFlags,
    int atRemoveDirFlag,
    TState state,
    BeforeDeleteEntryCallback<TState> beforeDelete,
    AfterDeleteEntryCallback<TState> afterDelete) {
    ArgumentNullException.ThrowIfNull(directory);
    ArgumentNullException.ThrowIfNull(entries);

    if (entries.Count == 0) {
      return true;
    }

    string dirFullName = directory.Path.GetFullName(_fullNameBufferPool);
    int dirFd = open(dirFullName, openDirectoryFlags);
    if (dirFd < 0) {
      int errno = Marshal.GetLastPInvokeError();
      throw new IOException($"Failed to open directory '{dirFullName}' for deletion: errno {errno}");
    }

    var allSuccess = true;
    try {
      for (int i = 0; i < entries.Count; i++) {
        var entry = entries[i];

        beforeDelete(entries, i, state);

        int flags = entry.IsRegularDirectory ? atRemoveDirFlag : 0;
        int res = unlinkat(dirFd, entry.Name, flags);
        if (res != 0) {
          int errno = Marshal.GetLastPInvokeError();
          if (errno != ENOENT) {
            var ex = new IOException($"Failed to unlink '{entry.Name}' in '{dirFullName}': errno {errno}");
            afterDelete(entries, i, ex, state);
            allSuccess = false;
          } else {
            afterDelete(entries, i, null, state);
          }
        } else {
          afterDelete(entries, i, null, state);
        }
      }
      return allSuccess;
    }
    finally {
      close(dirFd);
    }
  }
}
