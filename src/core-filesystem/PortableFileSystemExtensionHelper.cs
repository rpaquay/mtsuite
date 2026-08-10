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
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// Shared helper for standard, portable implementation of directory entry deletion
/// without native platform optimizations.
/// </summary>
public sealed class PortableFileSystemExtensionHelper {
  private readonly IPool<StringBuffer> _fullNameBufferPool;

  public PortableFileSystemExtensionHelper(IPool<StringBuffer> fullNameBufferPool) {
    ArgumentNullException.ThrowIfNull(fullNameBufferPool);
    _fullNameBufferPool = fullNameBufferPool;
  }

  public bool DeleteDirectoryEntries<TState>(
    FileSystemEntry directory,
    IReadOnlyList<FileSystemEntry> entries,
    ref TState state,
    BeforeDeleteEntryCallback<TState> beforeDelete,
    AfterDeleteEntryCallback<TState> afterDelete) {
    ArgumentNullException.ThrowIfNull(directory);
    ArgumentNullException.ThrowIfNull(entries);

    if (entries.Count == 0) {
      return true;
    }

    var allSuccess = true;
    for (int i = 0; i < entries.Count; i++) {
      var entry = entries[i];
      string fullName = entry.Path.GetFullName(_fullNameBufferPool);

      if (beforeDelete(entries, i, ref state)) {
        try {
          if (entry.IsFile || entry.IsReparsePoint) {
            if (entry.IsReadOnly) {
              try { File.SetAttributes(fullName, FileAttributes.Normal); } catch { }
            }
            File.Delete(fullName);
          } else if (entry.IsRegularDirectory) {
            Directory.Delete(fullName, recursive: false);
          }
          afterDelete(entries, i, null, ref state);
        } catch (Exception ex) {
          afterDelete(entries, i, ex, ref state);
          allSuccess = false;
        }
      }
    }
    return allSuccess;
  }
}
