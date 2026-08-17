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
using System.Collections.ObjectModel;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// Abstraction for platform- and filesystem-specific advanced operations,
/// such as Copy-on-Write (CoW) file cloning, extent querying, and platform optimizations.
/// </summary>
public interface IFileSystemExtension {
  /// <summary>
  /// Checks whether Copy-on-Write (CoW) file cloning is supported between <paramref name="sourcePath"/>
  /// and <paramref name="destinationPath"/> (e.g. APFS on macOS within the same volume).
  /// </summary>
  bool IsCloningSupported(FullPath sourcePath, FullPath destinationPath);
  
  /// <summary>
  /// Checks whether two files are Copy-on-Write (CoW) clones of each other (sharing physical disk extents).
  /// </summary>
  bool AreFilesCloned(FileSystemEntry file1, FileSystemEntry file2);

  /// <summary>
  /// Clones a file using Copy-on-Write (CoW) filesystem semantics.
  /// Throws an exception if cloning is not supported or fails.
  /// </summary>
  void CloneFile(FileSystemEntry sourceEntry, FullPath destinationPath);
  
  /// <summary>
  /// Delete all entries of <paramref name="directory"/> that are allowed by <paramref name="entries"/>,
  /// calling beforeDelete and afterDelete callbacks for each entry to avoid lambda delegate allocations.
  /// </summary>
  bool DeleteDirectoryEntries<TState>(
    FileSystemEntry directory,
    IReadOnlyList<FileSystemEntry> entries,
    ref TState state,
    BeforeDeleteEntryCallback<TState> beforeDelete,
    AfterDeleteEntryCallback<TState> afterDelete);

  /// <summary>
  /// Tries to inspect platform-specific reparse point tags (e.g. Windows directory junctions vs symlinks).
  /// Returns <c>true</c> if reparse tag information was successfully queried; otherwise <c>false</c>.
  /// </summary>
  bool TryGetReparsePointTag(string fullName, out bool isJunction, out bool isSymLink);
}

/// <summary>
/// Callback invoked before an entry is deleted.
/// </summary>
public delegate bool BeforeDeleteEntryCallback<TState>(
  IReadOnlyList<FileSystemEntry> entries,
  int entryIndex,
  ref TState state);

/// <summary>
/// Callback invoked after an entry is deleted (with an optional exception if it failed).
/// </summary>
public delegate void AfterDeleteEntryCallback<TState>(
  IReadOnlyList<FileSystemEntry> entries,
  int entryIndex,
  Exception? exception,
  ref TState state);
