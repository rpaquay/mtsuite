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
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// Windows implementation of <see cref="IFileSystemExtension"/> using ReFS / Dev Drive block cloning via <c>FSCTL_DUPLICATE_EXTENTS_TO_FILE</c>.
/// </summary>
public class WindowsFileSystemExtension : IFileSystemExtension {
  private const uint FSCTL_DUPLICATE_EXTENTS_TO_FILE = 0x00098344;
  private const uint FSCTL_GET_RETRIEVAL_POINTERS = 0x00090073;
  private const int ERROR_MORE_DATA = 234;

  [StructLayout(LayoutKind.Sequential)]
  private struct DUPLICATE_EXTENTS_DATA {
    public IntPtr FileHandle;
    public long SourceFileOffset;
    public long TargetFileOffset;
    public long ByteCount;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct STARTING_VCN_INPUT_BUFFER {
    public long StartingVcn;
  }

  [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool DeviceIoControl(
    SafeFileHandle hDevice,
    uint dwIoControlCode,
    ref DUPLICATE_EXTENTS_DATA lpInBuffer,
    uint nInBufferSize,
    IntPtr lpOutBuffer,
    uint nOutBufferSize,
    out uint lpBytesReturned,
    IntPtr lpOverlapped);

  [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool DeviceIoControl(
    SafeFileHandle hDevice,
    uint dwIoControlCode,
    ref STARTING_VCN_INPUT_BUFFER lpInBuffer,
    uint nInBufferSize,
    [Out] byte[] lpOutBuffer,
    uint nOutBufferSize,
    out uint lpBytesReturned,
    IntPtr lpOverlapped);

  [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetDiskFreeSpaceW(
    string lpRootPathName,
    out uint lpSectorsPerCluster,
    out uint lpBytesPerSector,
    out uint lpNumberOfFreeClusters,
    out uint lpTotalNumberOfClusters);

  private readonly IPool<StringBuffer> _fullNameBufferPool;
  private readonly PortableFileSystemExtensionHelper _portableHelper;

  public WindowsFileSystemExtension(MtPoolFactory poolFactory) {
    ArgumentNullException.ThrowIfNull(poolFactory);
    _fullNameBufferPool = poolFactory.Create("WindowsFileSystemExtension.FullNameBuffer",
      static () => new StringBuffer(), static sb => sb.Clear());
    _portableHelper = new PortableFileSystemExtensionHelper(_fullNameBufferPool);
  }

  private static int GetVolumeClusterSize(string path) {
    string? root = Path.GetPathRoot(path);
    if (!string.IsNullOrEmpty(root)) {
      if (!root.EndsWith('\\')) {
        root += '\\';
      }
      if (GetDiskFreeSpaceW(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _)) {
        long clusterBytes = (long)sectorsPerCluster * bytesPerSector;
        if (clusterBytes > 0 && clusterBytes <= int.MaxValue) {
          return (int)clusterBytes;
        }
      }
    }
    return 4096;
  }

  public bool IsCloningSupported(FullPath sourcePath, FullPath destinationPath) {
    if (!OperatingSystem.IsWindows()) {
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
        int clusterSize = GetVolumeClusterSize(probeSrc);
        int probeSize = Math.Max(clusterSize, 65536);
        byte[] probeData = new byte[probeSize];
        File.WriteAllBytes(probeSrc, probeData);

        using var srcHandle = File.OpenHandle(probeSrc, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dstHandle = File.OpenHandle(probeDst, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        RandomAccess.SetLength(dstHandle, probeSize);

        var dupData = new DUPLICATE_EXTENTS_DATA {
          FileHandle = srcHandle.DangerousGetHandle(),
          SourceFileOffset = 0,
          TargetFileOffset = 0,
          ByteCount = probeSize
        };

        bool success = DeviceIoControl(
          dstHandle,
          FSCTL_DUPLICATE_EXTENTS_TO_FILE,
          ref dupData,
          (uint)Marshal.SizeOf<DUPLICATE_EXTENTS_DATA>(),
          IntPtr.Zero,
          0,
          out _,
          IntPtr.Zero);

        return success;
      }
      finally {
        try { if (File.Exists(probeSrc)) File.Delete(probeSrc); } catch { }
        try { if (File.Exists(probeDst)) File.Delete(probeDst); } catch { }
      }
    }
    catch {
      return false;
    }
  }

  private readonly struct ExtentMapping {
    public readonly long StartVcn;
    public readonly long NextVcn;
    public readonly long Lcn;

    public ExtentMapping(long startVcn, long nextVcn, long lcn) {
      StartVcn = startVcn;
      NextVcn = nextVcn;
      Lcn = lcn;
    }
  }

  private static List<ExtentMapping> GetFileExtents(SafeFileHandle handle) {
    var list = new List<ExtentMapping>();
    var input = new STARTING_VCN_INPUT_BUFFER { StartingVcn = 0 };
    byte[] outBuffer = new byte[4096];

    while (true) {
      bool ok = DeviceIoControl(
        handle,
        FSCTL_GET_RETRIEVAL_POINTERS,
        ref input,
        (uint)Marshal.SizeOf<STARTING_VCN_INPUT_BUFFER>(),
        outBuffer,
        (uint)outBuffer.Length,
        out uint bytesReturned,
        IntPtr.Zero);

      int error = ok ? 0 : Marshal.GetLastPInvokeError();
      if (!ok && error != ERROR_MORE_DATA) {
        break;
      }

      if (bytesReturned < 16) {
        break;
      }

      uint extentCount = BitConverter.ToUInt32(outBuffer, 0);
      long currentVcn = BitConverter.ToInt64(outBuffer, 8);
      int offset = 16;
      for (uint i = 0; i < extentCount; i++) {
        if (offset + 16 > bytesReturned) break;
        long nextVcn = BitConverter.ToInt64(outBuffer, offset);
        long lcn = BitConverter.ToInt64(outBuffer, offset + 8);
        list.Add(new ExtentMapping(currentVcn, nextVcn, lcn));
        currentVcn = nextVcn;
        offset += 16;
      }

      if (error == ERROR_MORE_DATA && list.Count > 0) {
        input.StartingVcn = list[^1].NextVcn;
      }
      else {
        break;
      }
    }

    return list;
  }

  private static bool TryGetLcnForVcn(List<ExtentMapping> extents, long vcn, out long lcn) {
    foreach (var ext in extents) {
      if (vcn >= ext.StartVcn && vcn < ext.NextVcn) {
        if (ext.Lcn == -1) {
          lcn = -1;
          return true;
        }
        lcn = ext.Lcn + (vcn - ext.StartVcn);
        return true;
      }
    }
    lcn = -1;
    return false;
  }

  public bool AreFilesCloned(FileSystemEntry file1, FileSystemEntry file2) {
    if (!OperatingSystem.IsWindows()) {
      return false;
    }

    if (file1.FileSize != file2.FileSize) {
      return false;
    }

    // We "lie" here, because we assume the caller is going to make a decision about whether to clone file1 into file2
    if (file1.FileSize == 0) {
      return true;
    }

    try {
      string path1 = file1.Path.GetFullName(_fullNameBufferPool);
      string path2 = file2.Path.GetFullName(_fullNameBufferPool);

      using var handle1 = File.OpenHandle(path1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      using var handle2 = File.OpenHandle(path2, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

      var extents1 = GetFileExtents(handle1);
      var extents2 = GetFileExtents(handle2);

      if (extents1.Count == 0 || extents2.Count == 0) {
        return false;
      }

      int clusterSize = GetVolumeClusterSize(path1);
      long alignedBytes = (file1.FileSize / clusterSize) * clusterSize;
      long clustersToCheck = alignedBytes / clusterSize;

      if (clustersToCheck == 0) {
        return true;
      }

      for (long v = 0; v < clustersToCheck; v++) {
        if (!TryGetLcnForVcn(extents1, v, out long lcn1) ||
            !TryGetLcnForVcn(extents2, v, out long lcn2)) {
          return false;
        }
        if (lcn1 == -1 || lcn2 == -1 || lcn1 != lcn2) {
          return false;
        }
      }

      return true;
    }
    catch {
      return false;
    }
  }

  public void CloneFile(FileSystemEntry sourceEntry, FullPath destinationPath) {
    if (!OperatingSystem.IsWindows()) {
      throw new PlatformNotSupportedException("File cloning is currently only supported on Windows (ReFS).");
    }

    string srcFullName = sourceEntry.Path.GetFullName(_fullNameBufferPool);
    string dstFullName = destinationPath.GetFullName(_fullNameBufferPool);
    string destDir = Path.GetDirectoryName(dstFullName) ?? dstFullName;
    string tempDst = Path.Combine(destDir, ".mtcompact_tmp_" + Guid.NewGuid().ToString("N") + ".tmp");

    bool success = false;
    try {
      long fileSize = sourceEntry.FileSize;
      if (fileSize == 0) {
        using var _ = File.OpenHandle(tempDst, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
      }
      else {
        int clusterSize = GetVolumeClusterSize(srcFullName);
        long alignedBytes = (fileSize / clusterSize) * clusterSize;
        long tailBytes = fileSize - alignedBytes;

        using var srcHandle = File.OpenHandle(srcFullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dstHandle = File.OpenHandle(tempDst, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        RandomAccess.SetLength(dstHandle, fileSize);

        if (alignedBytes > 0) {
          const long maxChunkSize = 1024L * 1024 * 1024; // 1 GB chunk limit for ReFS
          long currentOffset = 0;
          while (currentOffset < alignedBytes) {
            long chunkSize = Math.Min(alignedBytes - currentOffset, maxChunkSize);
            chunkSize = (chunkSize / clusterSize) * clusterSize;
            if (chunkSize <= 0) break;

            var dupData = new DUPLICATE_EXTENTS_DATA {
              FileHandle = srcHandle.DangerousGetHandle(),
              SourceFileOffset = currentOffset,
              TargetFileOffset = currentOffset,
              ByteCount = chunkSize
            };

            if (!DeviceIoControl(
                  dstHandle,
                  FSCTL_DUPLICATE_EXTENTS_TO_FILE,
                  ref dupData,
                  (uint)Marshal.SizeOf<DUPLICATE_EXTENTS_DATA>(),
                  IntPtr.Zero,
                  0,
                  out _,
                  IntPtr.Zero)) {
              int win32Error = Marshal.GetLastPInvokeError();
              throw new IOException($"Failed to duplicate extents from '{srcFullName}' to '{dstFullName}' at offset {currentOffset}: Win32 error {win32Error}", new Win32Exception(win32Error));
            }

            currentOffset += chunkSize;
          }
        }

        if (tailBytes > 0) {
          byte[] tailBuffer = new byte[tailBytes];
          int bytesRead = RandomAccess.Read(srcHandle, tailBuffer, alignedBytes);
          RandomAccess.Write(dstHandle, tailBuffer.AsSpan(0, bytesRead), alignedBytes);
        }
      }

      // Preserve timestamps
      File.SetLastWriteTimeUtc(tempDst, sourceEntry.LastWriteTimeUtc);

      // Preserve FileAttributes
      if (sourceEntry.FileAttributes != FileAttributes.Normal) {
        File.SetAttributes(tempDst, sourceEntry.FileAttributes);
      }

      // Atomic replace
      File.Move(tempDst, dstFullName, overwrite: true);
      success = true;
    }
    finally {
      if (!success) {
        try { if (File.Exists(tempDst)) File.Delete(tempDst); } catch { }
      }
    }
  }

  public bool DeleteDirectoryEntries<TState>(
    FileSystemEntry directory,
    IReadOnlyList<FileSystemEntry> entries,
    ref TState state,
    BeforeDeleteEntryCallback<TState> beforeDelete,
    AfterDeleteEntryCallback<TState> afterDelete) =>
    _portableHelper.DeleteDirectoryEntries(directory, entries, ref state, beforeDelete, afterDelete);
}

