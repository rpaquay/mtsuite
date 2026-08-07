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
using System.IO;
using System.Runtime.InteropServices;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// macOS implementation of <see cref="IFileSystemExtension"/> using APFS native Copy-on-Write (CoW) cloning via <c>clonefile</c>.
/// </summary>
public class MacOSFileSystemExtension : IFileSystemExtension {
  [DllImport("libSystem", EntryPoint = "clonefile", SetLastError = true)]
  private static extern int clonefile(string src, string dst, uint flags);

  private readonly IPool<StringBuffer> _fullNameBufferPool;

  public MacOSFileSystemExtension(MtPoolFactory poolFactory) {
    ArgumentNullException.ThrowIfNull(poolFactory);
    _fullNameBufferPool = poolFactory.Create("MacOSFileSystemExtension.FullNameBuffer", static () => new StringBuffer(), static sb => sb.Clear());
  }

  public bool IsCloningSupported(FullPath sourcePath, FullPath destinationPath) {
    if (!OperatingSystem.IsMacOS()) {
      return false;
    }

    try {
      string srcFullName = sourcePath.GetFullName(_fullNameBufferPool);
      string dstFullName = destinationPath.GetFullName(_fullNameBufferPool);

      if (!Directory.Exists(srcFullName) && !File.Exists(srcFullName)) {
        return false;
      }
      if (!Directory.Exists(dstFullName) && !File.Exists(dstFullName)) {
        return false;
      }

      string sourceDir = Directory.Exists(srcFullName) ? srcFullName : (Path.GetDirectoryName(srcFullName) ?? srcFullName);
      string destDir = Directory.Exists(dstFullName) ? dstFullName : (Path.GetDirectoryName(dstFullName) ?? dstFullName);

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

    string srcFullName = sourceEntry.Path.GetFullName(_fullNameBufferPool);
    string dstFullName = destinationPath.GetFullName(_fullNameBufferPool);
    string destDir = Path.GetDirectoryName(dstFullName) ?? dstFullName;
    string tempDst = Path.Combine(destDir, ".mtcompact_tmp_" + Guid.NewGuid().ToString("N") + ".tmp");

    int res = clonefile(srcFullName, tempDst, 0);
    if (res != 0) {
      int errno = Marshal.GetLastPInvokeError();
      try { if (File.Exists(tempDst)) File.Delete(tempDst); } catch { }
      throw new IOException($"Failed to clone file '{srcFullName}' to '{dstFullName}': errno {errno}");
    }

    // Preserve timestamps
    try {
      File.SetLastWriteTimeUtc(tempDst, sourceEntry.LastWriteTimeUtc);
    } catch { }

    // Preserve Unix file modes (POSIX permissions)
    try {
      var mode = File.GetUnixFileMode(srcFullName);
      File.SetUnixFileMode(tempDst, mode);
    } catch { }

    // Preserve FileAttributes
    try {
      if (sourceEntry.FileAttributes != FileAttributes.Normal) {
        File.SetAttributes(tempDst, sourceEntry.FileAttributes);
      }
    } catch { }

    // Atomic replace
    File.Move(tempDst, dstFullName, overwrite: true);
  }

  public bool TryCloneFile<T>(
    FileSystemEntry sourceEntry,
    FullPath destinationPath,
    FileSystemEntry? destinationEntry,
    CopyFileOptions copyFileOptions,
    T param,
    CopyFileCallback<T> callback) {

    if (!OperatingSystem.IsMacOS()) {
      return false;
    }

    string dstFullName = destinationPath.GetFullName(_fullNameBufferPool);

    // If destination exists, clonefile fails with EEXIST unless deleted first
    if (destinationEntry.HasValue || File.Exists(dstFullName)) {
      try {
        File.Delete(dstFullName);
      } catch {
        // If destination cannot be deleted, fall back to streaming copy
        return false;
      }
    }

    string srcFullName = sourceEntry.Path.GetFullName(_fullNameBufferPool);
    int res = clonefile(srcFullName, dstFullName, 0);
    if (res != 0) {
      // clonefile failed (e.g. cross-volume copy or non-APFS volume), fall back to streaming copy
      return false;
    }

    // Preserve timestamps
    try {
      File.SetLastWriteTimeUtc(dstFullName, sourceEntry.LastWriteTimeUtc);
    } catch {
      // Best effort
    }

    // Preserve Unix file modes (POSIX permissions)
    try {
      var mode = File.GetUnixFileMode(srcFullName);
      File.SetUnixFileMode(dstFullName, mode);
    } catch {
      // Best effort
    }

    // Preserve FileAttributes
    try {
      if (sourceEntry.FileAttributes != FileAttributes.Normal) {
        File.SetAttributes(dstFullName, sourceEntry.FileAttributes);
      }
    } catch {
      // Best effort
    }

    // Notify callback that file copy is complete
    callback(ref sourceEntry, sourceEntry.FileSize, sourceEntry.FileSize, sourceEntry.FileSize, ref param);
    return true;
  }
}
