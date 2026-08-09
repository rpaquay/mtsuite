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

  // __fcntl is the non-variadic direct C entry point in libSystem.
  // Standard fcntl is a C variadic function (int fcntl(int, int, ...)) which on Apple Silicon (ARM64)
  // passes variadic arguments on the stack, causing standard P/Invoke register passing to crash with SIGSEGV 11.
  [DllImport("libSystem", EntryPoint = "__fcntl", SetLastError = true)]
  private static extern int __fcntl(int fd, int cmd, ref log2phys l2p);

  private const int F_LOG2PHYS_EXT = 65;

  [StructLayout(LayoutKind.Sequential, Pack = 4)]
  private struct log2phys {
    public uint l_flags;
    public long l_contigbytes;
    public long l_devoffset;
  }

  private readonly IPool<StringBuffer> _fullNameBufferPool;

  public MacOSFileSystemExtension(MtPoolFactory poolFactory) {
    ArgumentNullException.ThrowIfNull(poolFactory);
    _fullNameBufferPool = poolFactory.Create("MacOSFileSystemExtension.FullNameBuffer", static () => new StringBuffer(), static sb => sb.Clear());
  }


  public bool AreFilesCloned(FileSystemEntry file1, FileSystemEntry file2) {
    if (!OperatingSystem.IsMacOS()) {
      return false;
    }
    
    if (file1.FileSize != file2.FileSize) {
      return false;
    }
    
    // We "lie" here, because we assume the caller is going to make a decision about where to clone file1 into file2
    if (file1.FileSize == 0) {
      return true;
    }
    
    try {
      string path1 = file1.Path.GetFullName(_fullNameBufferPool);
      string path2 = file2.Path.GetFullName(_fullNameBufferPool);

      using var handle1 = File.OpenHandle(path1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      using var handle2 = File.OpenHandle(path2, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

      int fd1 = handle1.DangerousGetHandle().ToInt32();
      int fd2 = handle2.DangerousGetHandle().ToInt32();

      long fileSize = file1.FileSize;
      long currentOffset = 0;

      while (currentOffset < fileSize) {
        var ext1 = new log2phys { l_devoffset = currentOffset, l_contigbytes = fileSize - currentOffset };
        var ext2 = new log2phys { l_devoffset = currentOffset, l_contigbytes = fileSize - currentOffset };

        if (__fcntl(fd1, F_LOG2PHYS_EXT, ref ext1) != 0 || __fcntl(fd2, F_LOG2PHYS_EXT, ref ext2) != 0) {
          return false;
        }

        if (ext1.l_devoffset != ext2.l_devoffset) {
          return false;
        }

        long step = Math.Min(ext1.l_contigbytes, ext2.l_contigbytes);
        if (step <= 0) {
          return false;
        }
        currentOffset += step;
      }

      return currentOffset >= fileSize;
    } catch {
      return false;
    }
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

    bool success = false;
    try {
      int res = clonefile(srcFullName, tempDst, 0);
      if (res != 0) {
        int errno = Marshal.GetLastPInvokeError();
        throw new IOException($"Failed to clone file '{srcFullName}' to '{dstFullName}': errno {errno}");
      }

      // Preserve timestamps
      File.SetLastWriteTimeUtc(tempDst, sourceEntry.LastWriteTimeUtc);

      // Preserve Unix file modes (POSIX permissions)
      var mode = File.GetUnixFileMode(srcFullName);
      File.SetUnixFileMode(tempDst, mode);

      // Preserve FileAttributes
      if (sourceEntry.FileAttributes != FileAttributes.Normal) {
        File.SetAttributes(tempDst, sourceEntry.FileAttributes);
      }

      // Atomic replace
      File.Move(tempDst, dstFullName, overwrite: true);
      success = true;
    } finally {
      if (!success) {
        try { if (File.Exists(tempDst)) File.Delete(tempDst); } catch { }
      }
    }
  }
}
