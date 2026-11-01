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
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// Enumerates directory entries on Linux using <c>getdents64</c> and one <c>statx</c> per entry relative to
/// the open directory, so that the kernel does not have to walk the full path of every entry.
/// The produced <see cref="FileSystemEntry"/> values match those of the portable .NET enumerator.
/// </summary>
public sealed class LinuxDirectoryEnumerator {
  [DllImport("libc", EntryPoint = "open", SetLastError = true)]
  private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int oflag);

  [DllImport("libc", EntryPoint = "close", SetLastError = true)]
  private static extern int close(int fd);

  [DllImport("libc", EntryPoint = "getdents64", SetLastError = true)]
  private static extern unsafe nint getdents64(int fd, byte* buffer, nuint count);

  [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
  private static extern unsafe int statx(int dirfd, byte* path, int flags, uint mask, byte* statxBuffer);

  [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
  private static extern unsafe int statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, byte* statxBuffer);

  [DllImport("libc", EntryPoint = "geteuid")]
  private static extern uint geteuid();

  [DllImport("libc", EntryPoint = "getegid")]
  private static extern uint getegid();

  [DllImport("libc", EntryPoint = "getgroups", SetLastError = true)]
  private static extern unsafe int getgroups(int gidsetsize, uint* grouplist);

  private const int O_RDONLY = 0x0000;
  private const int O_CLOEXEC = 0x00080000;

  private const int AT_FDCWD = -100;
  private const int AT_SYMLINK_NOFOLLOW = 0x100;

  private const uint STATX_TYPE = 0x0001;
  private const uint STATX_MODE = 0x0002;
  private const uint STATX_UID = 0x0008;
  private const uint STATX_GID = 0x0010;
  private const uint STATX_MTIME = 0x0040;
  private const uint STATX_INO = 0x0100;
  private const uint STATX_SIZE = 0x0200;
  private const uint RequiredStatxFields = STATX_TYPE | STATX_MODE | STATX_UID | STATX_GID | STATX_MTIME | STATX_SIZE;

  // struct statx, which has the same layout on all architectures (unlike struct stat)
  private const int StatxSize = 256;
  private const int StatxMaskOffset = 0;
  private const int StatxUidOffset = 20;
  private const int StatxGidOffset = 24;
  private const int StatxModeOffset = 28;
  private const int StatxInodeOffset = 32;
  private const int StatxFileSizeOffset = 40;
  private const int StatxMtimeSecondsOffset = 112;
  private const int StatxMtimeNanosecondsOffset = 120;
  private const int StatxDeviceMajorOffset = 136;
  private const int StatxDeviceMinorOffset = 140;

  // struct linux_dirent64
  private const int DirentRecordLengthOffset = 16;
  private const int DirentNameOffset = 19;

  private const uint S_IFMT = 0xF000;
  private const uint S_IFREG = 0x8000;
  private const uint S_IFDIR = 0x4000;
  private const uint S_IFLNK = 0xA000;

  private const long UnixEpochFileTime = 116444736000000000L;

  public const int BufferSize = 64 * 1024;

  private readonly IPool<StringBuffer> _fullNameBufferPool;
  private readonly IPool<byte[]> _bufferPool;
  private readonly bool _isArchitectureSupported;
  private readonly uint _effectiveUserId;
  private readonly HashSet<uint> _groupIds;
  private volatile bool _isLibraryMissingFunctions;

  /// <summary>
  /// The value of <c>O_DIRECTORY</c>, which depends on the CPU architecture, or <c>null</c> if unknown.
  /// </summary>
  public static int? O_DIRECTORY => RuntimeInformation.ProcessArchitecture switch {
    Architecture.X64 => 0x00010000,
    Architecture.Arm64 => 0x00004000,
    _ => null,
  };

  public LinuxDirectoryEnumerator(IPool<StringBuffer> fullNameBufferPool, IPool<byte[]> bufferPool) {
    ArgumentNullException.ThrowIfNull(fullNameBufferPool);
    ArgumentNullException.ThrowIfNull(bufferPool);
    _fullNameBufferPool = fullNameBufferPool;
    _bufferPool = bufferPool;
    _isArchitectureSupported = O_DIRECTORY.HasValue;
    _effectiveUserId = geteuid();
    _groupIds = GetGroupIds();
  }

  private static unsafe HashSet<uint> GetGroupIds() {
    var result = new HashSet<uint> { getegid() };
    int count = getgroups(0, null);
    if (count > 0) {
      var groups = new uint[count];
      fixed (uint* p = groups) {
        count = getgroups(count, p);
      }
      for (var i = 0; i < count; i++) {
        result.Add(groups[i]);
      }
    }
    return result;
  }

  /// <summary>
  /// Appends the entries of <paramref name="directory"/> to <paramref name="entries"/>. Returns <c>false</c>
  /// (leaving <paramref name="entries"/> unchanged) if anything unexpected happens, so that the caller can
  /// fall back to the portable enumerator (which also takes care of reporting errors).
  /// </summary>
  public bool TryGetDirectoryEntries(FullPath directory, List<FileSystemEntry> entries) {
    if (!_isArchitectureSupported || _isLibraryMissingFunctions) {
      return false;
    }

    try {
      return TryGetDirectoryEntriesCore(directory, entries);
    }
    catch (EntryPointNotFoundException) {
      // libc is too old for getdents64 or statx (or is not glibc): don't try again.
      _isLibraryMissingFunctions = true;
      return false;
    }
  }

  /// <summary>
  /// Gets the device and inode of <paramref name="path"/>, following symbolic links.
  /// </summary>
  public unsafe bool TryGetDirectoryIdentity(FullPath path, out DirectoryIdentity identity) {
    identity = default;
    if (_isLibraryMissingFunctions) {
      return false;
    }

    byte* status = stackalloc byte[StatxSize];
    try {
      if (statx(AT_FDCWD, path.GetFullName(_fullNameBufferPool), 0, STATX_INO, status) != 0) {
        return false;
      }
    }
    catch (EntryPointNotFoundException) {
      _isLibraryMissingFunctions = true;
      return false;
    }
    if ((Read<uint>(status + StatxMaskOffset) & STATX_INO) == 0) {
      return false;
    }

    ulong device = (ulong)Read<uint>(status + StatxDeviceMajorOffset) << 32 | Read<uint>(status + StatxDeviceMinorOffset);
    identity = new DirectoryIdentity(device, Read<ulong>(status + StatxInodeOffset), 0);
    return true;
  }

  private unsafe bool TryGetDirectoryEntriesCore(FullPath directory, List<FileSystemEntry> entries) {
    int fd = open(directory.GetFullName(_fullNameBufferPool), O_RDONLY | O_DIRECTORY!.Value | O_CLOEXEC);
    if (fd < 0) {
      return false;
    }

    var startCount = entries.Count;
    var success = false;
    try {
      using var buffer = _bufferPool.AllocateFrom();
      fixed (byte* bufferStart = buffer.Item) {
        while (true) {
          nint byteCount = getdents64(fd, bufferStart, (nuint)buffer.Item.Length);
          if (byteCount < 0) {
            return false;
          }
          if (byteCount == 0) {
            break;
          }

          byte* entryStart = bufferStart;
          byte* bufferEnd = bufferStart + byteCount;
          while (entryStart < bufferEnd) {
            if (!TryParseEntry(directory, fd, entryStart, entries)) {
              return false;
            }
            entryStart += Read<ushort>(entryStart + DirentRecordLengthOffset);
          }
        }
      }

      success = true;
      return true;
    }
    finally {
      if (!success) {
        entries.RemoveRange(startCount, entries.Count - startCount);
      }
      close(fd);
    }
  }

  private unsafe bool TryParseEntry(FullPath directory, int directoryFd, byte* entryStart, List<FileSystemEntry> entries) {
    byte* nameStart = entryStart + DirentNameOffset;
    var nameBytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(nameStart);
    if (nameBytes.SequenceEqual("."u8) || nameBytes.SequenceEqual(".."u8)) {
      return true;
    }

    byte* status = stackalloc byte[StatxSize];
    if (statx(directoryFd, nameStart, AT_SYMLINK_NOFOLLOW, RequiredStatxFields, status) != 0) {
      return false;
    }
    if ((Read<uint>(status + StatxMaskOffset) & RequiredStatxFields) != RequiredStatxFields) {
      return false;
    }

    uint mode = Read<ushort>(status + StatxModeOffset);
    uint fileType = mode & S_IFMT;
    var isDirectory = fileType == S_IFDIR;
    var isSymLink = fileType == S_IFLNK;
    if (!isDirectory && !isSymLink && fileType != S_IFREG) {
      // Let the portable enumerator deal with sockets, fifos, devices, etc.
      return false;
    }

    long seconds = Read<long>(status + StatxMtimeSecondsOffset);
    uint nanoseconds = Read<uint>(status + StatxMtimeNanosecondsOffset);
    long lastWriteTimeUtc = seconds * TimeSpan.TicksPerSecond + nanoseconds / 100 + UnixEpochFileTime;
    if (lastWriteTimeUtc < 0) {
      return false;
    }

    // Match .NET: a symbolic link to a directory is reported as a directory.
    if (isSymLink) {
      byte* target = stackalloc byte[StatxSize];
      isDirectory = statx(directoryFd, nameStart, 0, STATX_TYPE, target) == 0 &&
                    (Read<ushort>(target + StatxModeOffset) & S_IFMT) == S_IFDIR;
    }

    var attributes = (FileAttributes)0;
    if (IsReadOnly(Read<uint>(status + StatxUidOffset), Read<uint>(status + StatxGidOffset), mode)) {
      attributes |= FileAttributes.ReadOnly;
    }
    if (isSymLink) {
      attributes |= FileAttributes.ReparsePoint;
    }
    if (isDirectory) {
      attributes |= FileAttributes.Directory;
    }
    if (nameBytes[0] == (byte)'.') {
      attributes |= FileAttributes.Hidden;
    }
    if (attributes == 0) {
      attributes = FileAttributes.Normal;
    }

    var length = (isDirectory || isSymLink) ? 0 : Read<long>(status + StatxFileSizeOffset);
    var data = new FileSystemEntryData(attributes, length, lastWriteTimeUtc);
    entries.Add(new FileSystemEntry(new FullPath(directory, Encoding.UTF8.GetString(nameBytes)), data));
    return true;
  }

  /// <summary>
  /// Same logic as .NET's <c>FileStatus.IsModeReadOnlyCore</c> on Unix.
  /// </summary>
  private bool IsReadOnly(uint ownerId, uint groupId, uint mode) {
    const uint userRead = 0x100, userWrite = 0x80;
    const uint groupRead = 0x20, groupWrite = 0x10;
    const uint otherRead = 0x4, otherWrite = 0x2;

    bool isUserReadOnly = (mode & userRead) != 0 && (mode & userWrite) == 0;
    bool isGroupReadOnly = (mode & groupRead) != 0 && (mode & groupWrite) == 0;
    bool isOtherReadOnly = (mode & otherRead) != 0 && (mode & otherWrite) == 0;

    if (isUserReadOnly && isGroupReadOnly && isOtherReadOnly) {
      return true;
    }
    if (ownerId == _effectiveUserId) {
      return isUserReadOnly;
    }
    if (_groupIds.Contains(groupId)) {
      return isGroupReadOnly;
    }
    return isOtherReadOnly;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static unsafe T Read<T>(byte* p) where T : unmanaged => Unsafe.ReadUnaligned<T>(p);
}
