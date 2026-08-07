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
  /// Clones a file using Copy-on-Write (CoW) filesystem semantics.
  /// Throws an exception if cloning is not supported or fails.
  /// </summary>
  void CloneFile(FileSystemEntry sourceEntry, FullPath destinationPath);

  /// <summary>
  /// Attempts to clone a file during copy operations.
  /// Returns true if the file was successfully cloned and callback invoked, false otherwise.
  /// </summary>
  bool TryCloneFile<T>(
    FileSystemEntry sourceEntry,
    FullPath destinationPath,
    FileSystemEntry? destinationEntry,
    CopyFileOptions copyFileOptions,
    T param,
    CopyFileCallback<T> callback);
}
