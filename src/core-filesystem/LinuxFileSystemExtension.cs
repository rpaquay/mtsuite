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
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// Linux implementation of <see cref="IFileSystemExtension"/>.
/// </summary>
public class LinuxFileSystemExtension : IFileSystemExtension {
  private const int O_RDONLY = 0x0000;
  private const int O_DIRECTORY = 0x00010000;
  private const int O_CLOEXEC = 0x00080000;
  private const int AT_REMOVEDIR = 0x0200;

  private readonly IPool<StringBuffer> _fullNameBufferPool;
  private readonly PosixFileSystemExtension _posix;

  public LinuxFileSystemExtension(MtPoolFactory poolFactory) {
    ArgumentNullException.ThrowIfNull(poolFactory);
    _fullNameBufferPool = poolFactory.Create("LinuxFileSystemExtension.FullNameBuffer", static () => new StringBuffer(), static sb => sb.Clear());
    _posix = new PosixFileSystemExtension(_fullNameBufferPool);
  }

  public bool IsCloningSupported(FullPath sourcePath, FullPath destinationPath) {
    // Linux FICLONE / ioctl support can be probed here when enabled for Btrfs/XFS
    return false;
  }

  public bool AreFilesCloned(FileSystemEntry file1, FileSystemEntry file2) => false;

  public void CloneFile(FileSystemEntry sourceEntry, FullPath destinationPath) {
    throw new PlatformNotSupportedException("File cloning is not yet implemented for Linux (FICLONE).");
  }

  public bool DeleteDirectoryEntries<TState>(
    FileSystemEntry directory,
    IReadOnlyList<FileSystemEntry> entries,
    ref TState state,
    BeforeDeleteEntryCallback<TState> beforeDelete,
    AfterDeleteEntryCallback<TState> afterDelete) =>
    _posix.DeleteDirectoryEntries(directory, entries, O_RDONLY | O_DIRECTORY | O_CLOEXEC, AT_REMOVEDIR, ref state, beforeDelete, afterDelete);

  public bool TryGetReparsePointTag(string fullName, out bool isJunction, out bool isSymLink) {
    isJunction = false;
    isSymLink = false;
    return false;
  }
}
