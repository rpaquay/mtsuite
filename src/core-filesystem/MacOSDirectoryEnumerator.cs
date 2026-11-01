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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// Enumerates directory entries on macOS using <c>getattrlistbulk</c>, which returns the name, type,
/// size, timestamps and permissions of many entries per system call (instead of one <c>lstat</c> per entry).
/// The produced <see cref="FileSystemEntry"/> values match those of the portable .NET enumerator.
/// </summary>
public sealed class MacOSDirectoryEnumerator {
  [DllImport("libSystem", EntryPoint = "open", SetLastError = true)]
  private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int oflag);

  [DllImport("libSystem", EntryPoint = "close", SetLastError = true)]
  private static extern int close(int fd);

  [DllImport("libSystem", EntryPoint = "getattrlistbulk", SetLastError = true)]
  private static extern unsafe int getattrlistbulk(int dirfd, ref AttrList attrList, byte* attrBuf, nuint attrBufSize, ulong options);

  [DllImport("libSystem", EntryPoint = "getattrlist", SetLastError = true)]
  private static extern unsafe int getattrlist([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref AttrList attrList, byte* attrBuf, nuint attrBufSize, uint options);

  [DllImport("libSystem", EntryPoint = "geteuid")]
  private static extern uint geteuid();

  [DllImport("libSystem", EntryPoint = "getegid")]
  private static extern uint getegid();

  [DllImport("libSystem", EntryPoint = "getgroups", SetLastError = true)]
  private static extern unsafe int getgroups(int gidsetsize, uint* grouplist);

  private const int O_RDONLY = 0x0000;
  private const int O_DIRECTORY = 0x00100000;
  private const int O_CLOEXEC = 0x01000000;

  private const ushort ATTR_BIT_MAP_COUNT = 5;
  private const uint ATTR_CMN_NAME = 0x00000001;
  private const uint ATTR_CMN_DEVID = 0x00000002;
  private const uint ATTR_CMN_OBJTYPE = 0x00000008;
  private const uint ATTR_CMN_MODTIME = 0x00000400;
  private const uint ATTR_CMN_OWNERID = 0x00008000;
  private const uint ATTR_CMN_GRPID = 0x00010000;
  private const uint ATTR_CMN_ACCESSMASK = 0x00020000;
  private const uint ATTR_CMN_FLAGS = 0x00040000;
  private const uint ATTR_CMN_FILEID = 0x02000000;
  private const uint ATTR_CMN_ERROR = 0x20000000;
  private const uint ATTR_CMN_RETURNED_ATTRS = 0x80000000;
  private const uint ATTR_FILE_DATALENGTH = 0x00000200;

  // Attributes we need for every entry (ATTR_FILE_DATALENGTH is only returned for files).
  private const uint RequiredCommonAttrs = ATTR_CMN_NAME | ATTR_CMN_OBJTYPE | ATTR_CMN_MODTIME |
                                           ATTR_CMN_OWNERID | ATTR_CMN_GRPID | ATTR_CMN_ACCESSMASK |
                                           ATTR_CMN_FLAGS;

  // fsobj_type_t values (sys/vnode.h)
  private const uint VREG = 1;
  private const uint VDIR = 2;
  private const uint VLNK = 5;

  // st_flags (sys/stat.h)
  private const uint UF_HIDDEN = 0x00008000;

  private const long UnixEpochFileTime = 116444736000000000L;

  public const int BufferSize = 256 * 1024;

  [StructLayout(LayoutKind.Sequential)]
  private struct AttrList {
    public ushort bitmapcount;
    public ushort reserved;
    public uint commonattr;
    public uint volattr;
    public uint dirattr;
    public uint fileattr;
    public uint forkattr;
  }

  private readonly IPool<StringBuffer> _fullNameBufferPool;
  private readonly IPool<byte[]> _bufferPool;
  private readonly uint _effectiveUserId;
  private readonly HashSet<uint> _groupIds;

  public MacOSDirectoryEnumerator(IPool<StringBuffer> fullNameBufferPool, IPool<byte[]> bufferPool) {
    ArgumentNullException.ThrowIfNull(fullNameBufferPool);
    ArgumentNullException.ThrowIfNull(bufferPool);
    _fullNameBufferPool = fullNameBufferPool;
    _bufferPool = bufferPool;
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
  public unsafe bool TryGetDirectoryEntries(FullPath directory, List<FileSystemEntry> entries) {
    var fullName = directory.GetFullName(_fullNameBufferPool);
    int fd = open(fullName, O_RDONLY | O_DIRECTORY | O_CLOEXEC);
    if (fd < 0) {
      return false;
    }

    var startCount = entries.Count;
    var success = false;
    try {
      var attrList = new AttrList {
        bitmapcount = ATTR_BIT_MAP_COUNT,
        commonattr = ATTR_CMN_RETURNED_ATTRS | ATTR_CMN_ERROR | RequiredCommonAttrs,
        fileattr = ATTR_FILE_DATALENGTH,
      };

      using var buffer = _bufferPool.AllocateFrom();
      fixed (byte* bufferStart = buffer.Item) {
        while (true) {
          int count = getattrlistbulk(fd, ref attrList, bufferStart, (nuint)buffer.Item.Length, 0);
          if (count < 0) {
            return false;
          }
          if (count == 0) {
            break;
          }

          byte* entryStart = bufferStart;
          for (var i = 0; i < count; i++) {
            uint entryLength = Read<uint>(entryStart);
            if (!TryParseEntry(directory, fullName, entryStart, entries)) {
              return false;
            }
            entryStart += entryLength;
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

  /// <summary>
  /// Gets the device and file id of <paramref name="path"/>, following symbolic links.
  /// </summary>
  public unsafe bool TryGetDirectoryIdentity(FullPath path, out DirectoryIdentity identity) {
    identity = default;
    var attrList = new AttrList {
      bitmapcount = ATTR_BIT_MAP_COUNT,
      commonattr = ATTR_CMN_DEVID | ATTR_CMN_FILEID,
    };

    // Layout: length, then attributes in bit order: dev_t (32 bits), file id (64 bits)
    byte* buffer = stackalloc byte[16];
    if (getattrlist(path.GetFullName(_fullNameBufferPool), ref attrList, buffer, 16, 0) != 0) {
      return false;
    }
    identity = new DirectoryIdentity(Read<uint>(buffer + 4), Read<ulong>(buffer + 8), 0);
    return true;
  }

  private unsafe bool TryParseEntry(FullPath directory, string directoryFullName, byte* entryStart, List<FileSystemEntry> entries) {
    // Layout: length, attribute_set_t, then attributes in bit order (ATTR_CMN_ERROR first)
    byte* p = entryStart + sizeof(uint);
    uint returnedCommon = Read<uint>(p);
    uint returnedFile = Read<uint>(p + 12);
    p += 5 * sizeof(uint);

    if ((returnedCommon & ATTR_CMN_ERROR) != 0) {
      if (Read<uint>(p) != 0) {
        return false;
      }
      p += sizeof(uint);
    }
    if ((returnedCommon & RequiredCommonAttrs) != RequiredCommonAttrs) {
      return false;
    }

    // ATTR_CMN_NAME: attrreference_t { int32 offset (relative to itself); uint32 length (incl. NUL) }
    int nameOffset = Read<int>(p);
    uint nameLength = Read<uint>(p + 4);
    var name = nameLength > 1 ? Encoding.UTF8.GetString(p + nameOffset, (int)nameLength - 1) : "";
    p += 8;

    uint objType = Read<uint>(p);
    p += sizeof(uint);

    // ATTR_CMN_MODTIME: struct timespec
    long seconds = Read<long>(p);
    long nanoseconds = Read<long>(p + 8);
    p += 16;

    uint ownerId = Read<uint>(p);
    uint groupId = Read<uint>(p + 4);
    uint accessMask = Read<uint>(p + 8);
    uint flags = Read<uint>(p + 12);
    p += 16;

    long fileSize = 0;
    if ((returnedFile & ATTR_FILE_DATALENGTH) != 0) {
      fileSize = Read<long>(p);
    }

    long lastWriteTimeUtc = seconds * TimeSpan.TicksPerSecond + nanoseconds / 100 + UnixEpochFileTime;
    if (lastWriteTimeUtc < 0) {
      return false;
    }

    var isDirectory = objType == VDIR;
    var isSymLink = objType == VLNK;
    if (!isDirectory && !isSymLink && objType != VREG) {
      // Let the portable enumerator deal with sockets, fifos, devices, etc.
      return false;
    }

    // Match .NET: a symbolic link to a directory is reported as a directory.
    if (isSymLink && Directory.Exists(Path.Combine(directoryFullName, name))) {
      isDirectory = true;
    }

    var attributes = (FileAttributes)0;
    if (IsReadOnly(ownerId, groupId, accessMask)) {
      attributes |= FileAttributes.ReadOnly;
    }
    if (isSymLink) {
      attributes |= FileAttributes.ReparsePoint;
    }
    if (isDirectory) {
      attributes |= FileAttributes.Directory;
    }
    if (name.StartsWith('.') || (flags & UF_HIDDEN) != 0) {
      attributes |= FileAttributes.Hidden;
    }
    if (attributes == 0) {
      attributes = FileAttributes.Normal;
    }

    var length = (isDirectory || isSymLink) ? 0 : fileSize;
    var data = new FileSystemEntryData(attributes, length, lastWriteTimeUtc);
    entries.Add(new FileSystemEntry(new FullPath(directory, name), data));
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
