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
/// Factory helpers for <see cref="IFileSystemExtension"/>.
/// </summary>
public static class FileSystemExtension {
  /// <summary>
  /// Creates the appropriate <see cref="IFileSystemExtension"/> for the current runtime platform.
  /// </summary>
  public static IFileSystemExtension Create(MtPoolFactory poolFactory) {
    ArgumentNullException.ThrowIfNull(poolFactory);
    if (OperatingSystem.IsMacOS()) {
      return new MacOSFileSystemExtension(poolFactory);
    }
    if (OperatingSystem.IsLinux()) {
      return new LinuxFileSystemExtension(poolFactory);
    }
    if (OperatingSystem.IsWindows()) {
      return new WindowsFileSystemExtension(poolFactory);
    }
    return new NullFileSystemExtension(poolFactory);
  }
}
