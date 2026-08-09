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
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// Windows implementation of <see cref="IFileSystemExtension"/>.
/// </summary>
public class WindowsFileSystemExtension : IFileSystemExtension {
  private readonly MtPoolFactory _poolFactory;

  public WindowsFileSystemExtension(MtPoolFactory poolFactory) {
    ArgumentNullException.ThrowIfNull(poolFactory);
    _poolFactory = poolFactory;
  }

  public bool IsCloningSupported(FullPath sourcePath, FullPath destinationPath) {
    // Windows ReFS block cloning (FSCTL_DUPLICATE_EXTENTS_TO_FILE) can be probed here
    return false;
  }


  public bool AreFilesCloned(FileSystemEntry file1, FileSystemEntry file2) => false;

  public void CloneFile(FileSystemEntry sourceEntry, FullPath destinationPath) {
    throw new PlatformNotSupportedException("File cloning is not yet implemented for Windows (ReFS block cloning).");
  }
}
