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
using System.Collections.Generic;
using System.IO.Enumeration;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

public class FileSystemPortable : IFileSystem {

  private readonly IPool<List<FileSystemEntry>> _entryListPool;
  private readonly IPool<byte[]> _copyFileBufferPool;
  private readonly IPool<StringBuffer> _fullNameBufferPool;
  private readonly IFileSystemExtension _extension;


  public FileSystemPortable(MtPoolFactory poolFactory, IFileSystemExtension extension) {
    ArgumentNullException.ThrowIfNull(poolFactory);
    ArgumentNullException.ThrowIfNull(extension);
    _extension = extension;
    _entryListPool = poolFactory.CreateList<FileSystemEntry>("FileSystemPortable.EntryList");
    _copyFileBufferPool =
      poolFactory.Create("FileIOByteArrayPool", static () => new byte[FileIOByteArrayPool.BufferSize]);
    _fullNameBufferPool = poolFactory.Create("FileSystemPortable.FullNameBuffer", static () => new StringBuffer(),
      static sb => sb.Clear());
  }

  public IFileSystemExtension Extension => _extension;

  private readonly EnumerationOptions _enumerationOptions = new EnumerationOptions {
    RecurseSubdirectories = false,
    AttributesToSkip = FileAttributes.None,
    IgnoreInaccessible = false,
    ReturnSpecialDirectories = false
  };

  public FileSystemEntry GetEntry(FullPath path) {
    ArgumentNullException.ThrowIfNull(path);
    if (!TryGetEntry(path, out var entry)) {
      var fullPath = path.GetFullName(_fullNameBufferPool);
      throw new FileNotFoundException($"File or directory \"{fullPath}\" not found", fullPath);
    }

    return entry;
  }

  public bool TryGetEntry(FullPath path, out FileSystemEntry entry) {
    ArgumentNullException.ThrowIfNull(path);
    var fullName = path.GetFullName(_fullNameBufferPool);
    var fileInfo = new FileInfo(fullName);
    var attributes = fileInfo.Attributes;

    if ((int)attributes == -1) {
      // Double check LinkTarget for broken symlinks or DirectoryInfo
      if (fileInfo.LinkTarget != null) {
        attributes = FileAttributes.ReparsePoint;
      }
      else {
        entry = default;
        return false;
      }
    }

    var isDir = (attributes & FileAttributes.Directory) != 0;
    var isReparse = (attributes & FileAttributes.ReparsePoint) != 0 || fileInfo.LinkTarget != null;
    var length = (isDir || isReparse) ? 0 : (fileInfo.Exists ? fileInfo.Length : 0);

    long fileTimeUtc = 0;
    try {
      fileTimeUtc = fileInfo.LastWriteTimeUtc.ToFileTimeUtc();
    }
    catch {
      // Fallback for timestamps outside FILETIME range
    }

    var data = new FileSystemEntryData(attributes, length, fileTimeUtc);
    entry = new FileSystemEntry(path, data);
    return true;
  }

  public ReparsePointInfo GetReparsePointInfo(FullPath path) {
    ArgumentNullException.ThrowIfNull(path);
    var fullName = path.GetFullName(_fullNameBufferPool);
    var info = new FileInfo(fullName);

    // On .NET 6+, LinkTarget retrieves the link target even for broken symlinks
    var target = info.LinkTarget ?? (Directory.Exists(fullName) ? new DirectoryInfo(fullName).LinkTarget : null);

    if (target == null && (info.Attributes & FileAttributes.ReparsePoint) == 0) {
      throw new FileNotFoundException($"\"{fullName}\" was not found or was not a reparse point", fullName);
    }

    bool isJunction = false;
    bool isSymLink = target != null;

    if (target != null && target.StartsWith(@"\??\")) {
      target = target.Substring(@"\??\".Length);
    }

    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) {
      if (_extension.TryGetReparsePointTag(fullName, out var extensionIsJunction, out var extensionIsSymLink)) {
        isJunction = extensionIsJunction;
        isSymLink = extensionIsSymLink;
      }
    }

    return new ReparsePointInfo {
      IsJunctionPoint = isJunction,
      IsSymbolicLink = isSymLink,
      Target = target,
      IsTargetRelative = target != null && !Path.IsPathRooted(target),
      CreationTimeUtc = info.CreationTimeUtc,
      LastAccessTimeUtc = info.LastAccessTimeUtc,
      LastWriteTimeUtc = info.LastWriteTimeUtc,
    };
  }

  private sealed class DirectoryEntriesEnumerator : FileSystemEnumerator<FileSystemEntry> {
    private readonly FullPath _basePath;

    public DirectoryEntriesEnumerator(FullPath basePath, IPool<StringBuffer> fullNameBufferPool,
      EnumerationOptions options)
      : base(basePath.GetFullName(fullNameBufferPool), options) {
      _basePath = basePath;
    }

    protected override bool ShouldIncludeEntry(ref System.IO.Enumeration.FileSystemEntry entry) {
      return true;
    }

    protected override bool ShouldRecurseIntoEntry(ref System.IO.Enumeration.FileSystemEntry entry) {
      return false;
    }

    protected override FileSystemEntry TransformEntry(ref System.IO.Enumeration.FileSystemEntry fsEntry) {
      var fileName = fsEntry.FileName.ToString();
      var entryPath = new FullPath(_basePath, fileName);
      var isDir = fsEntry.IsDirectory;
      var isReparse = (fsEntry.Attributes & FileAttributes.ReparsePoint) != 0;
      var length = (isDir || isReparse) ? 0 : fsEntry.Length;
      var data = new FileSystemEntryData(fsEntry.Attributes, length,
        fsEntry.LastWriteTimeUtc.UtcDateTime.ToFileTimeUtc());
      return new FileSystemEntry(entryPath, data);
    }
  }

  public FromPool<List<FileSystemEntry>> GetDirectoryFiles(FullPath path) {
    ArgumentNullException.ThrowIfNull(path);
    var list = _entryListPool.AllocateFrom();
    try {
      using var enumerator = new DirectoryEntriesEnumerator(path, _fullNameBufferPool, _enumerationOptions);
      while (enumerator.MoveNext()) {
        list.Item.Add(enumerator.Current);
      }
    }
    catch {
      list.Dispose();
      throw;
    }

    return list;
  }

  public void CreateDirectory(FullPath path) {
    ArgumentNullException.ThrowIfNull(path);
    Directory.CreateDirectory(path.GetFullName(_fullNameBufferPool));
  }

  public void DeleteEntry(FileSystemEntry entry) {
    RemoveAccessDeniedAttributes(entry);
    if (entry.IsDirectory) {
      Directory.Delete(entry.Path.GetFullName(_fullNameBufferPool), recursive: false);
    }
    else {
      File.Delete(entry.Path.GetFullName(_fullNameBufferPool));
    }
  }

  public void CopyFile<T>(FileSystemEntry sourceEntry, FileSystemEntry destinationEntry, CopyFileOptions options,
    T param, CopyFileCallback<T> callback) {
    CopyFileWorker(sourceEntry, destinationEntry.Path, destinationEntry, options, param, callback);
  }

  public void CopyFile<T>(FileSystemEntry sourceEntry, FullPath destinationPath, CopyFileOptions options, T param,
    CopyFileCallback<T> callback) {
    if (TryGetEntry(destinationPath, out var destinationEntry)) {
      CopyFileWorker(sourceEntry, destinationPath, destinationEntry, options, param, callback);
    }
    else {
      CopyFileWorker(sourceEntry, destinationPath, null, options, param, callback);
    }
  }

  private void CopyFileWorker<T>(FileSystemEntry sourceEntry, FullPath destinationPath,
    FileSystemEntry? destinationEntry, CopyFileOptions options, T param, CopyFileCallback<T> callback) {
    // If the source is a reparse point, delete the destination and copy the reparse point.
    if (sourceEntry.IsReparsePoint) {
      if (destinationEntry.HasValue) {
        try {
          DeleteEntry(destinationEntry.Value);
        }
        catch {
          // Nothing to do here, as CopyDirectoryReparsePoint will report an exception below.
        }
      }

      if (sourceEntry.IsDirectory) {
        CopyDirectoryReparsePoint(sourceEntry.Path, destinationPath);
      }
      else {
        CopyFileReparsePoint(sourceEntry.Path, destinationPath);
      }
    }
    else {
      // If destination exists and is read-only, remove the read-only attribute
      if (destinationEntry.HasValue) {
        try {
          RemoveAccessDeniedAttributes(destinationEntry.Value);
        }
        catch {
          // Nothing to do here, as CopyFile will report an exception below.
        }
      }

      CopyFileImpl(sourceEntry, destinationPath, options, param, callback);
    }
  }

  private void CopyFileImpl<T>(FileSystemEntry sourceEntry, FullPath destinationPath, CopyFileOptions copyFileOptions,
    T param, CopyFileCallback<T> callback) {
    string srcFullName = sourceEntry.Path.GetFullName(_fullNameBufferPool);
    string dstFullName = destinationPath.GetFullName(_fullNameBufferPool);

    using (var buffer = _copyFileBufferPool.AllocateFrom())
    using (var sourceStream = new FileStream(srcFullName, FileMode.Open, FileAccess.Read, FileShare.Read, 0,
             FileOptions.SequentialScan))
    using (var destinationStream = new FileStream(dstFullName, FileMode.Create, FileAccess.Write, FileShare.None, 0,
             FileOptions.SequentialScan)) {

      // Pre-allocate destination file size on SSD / filesystem
      if (sourceEntry.FileSize > 0) {
        try {
          destinationStream.SetLength(sourceEntry.FileSize);
        }
        catch {
          // Best effort for filesystems that do not support pre-allocation
        }
      }

      long totalBytesReadSoFar = 0;
      int bytesRead;
      while ((bytesRead = sourceStream.Read(buffer.Item, 0, buffer.Item.Length)) > 0) {
        destinationStream.Write(buffer.Item, 0, bytesRead);
        totalBytesReadSoFar += bytesRead;
        callback(ref sourceEntry, bytesRead, totalBytesReadSoFar, sourceEntry.FileSize, ref param);
      }

      // If file was empty, invoke callback at least once
      if (totalBytesReadSoFar == 0) {
        callback(ref sourceEntry, 0, 0, 0, ref param);
      }

      // File may have changed size during copy, this is an error
      if (totalBytesReadSoFar != sourceEntry.FileSize) {
        throw new IOException(
          $"Size of source file has changed during copy ({totalBytesReadSoFar} != {sourceEntry.FileSize})");
      }
    }

    // Preserve timestamps
    try {
      File.SetLastWriteTimeUtc(dstFullName, sourceEntry.LastWriteTimeUtc);
    }
    catch {
      // Best effort
    }

    // Preserve Unix file modes (POSIX permissions)
    if (!OperatingSystem.IsWindows()) {
      try {
        var mode = File.GetUnixFileMode(srcFullName);
        File.SetUnixFileMode(dstFullName, mode);
      }
      catch {
        // Best effort
      }
    }

    // Preserve FileAttributes (ReadOnly applied last so write streams aren't blocked)
    try {
      if (sourceEntry.FileAttributes != FileAttributes.Normal) {
        File.SetAttributes(dstFullName, sourceEntry.FileAttributes);
      }
    }
    catch {
      // Best effort
    }
  }

  public FileStream OpenFile(FullPath path, FileAccess access) {
    ArgumentNullException.ThrowIfNull(path);
    return File.Open(path.GetFullName(_fullNameBufferPool), FileMode.Open, access, FileShare.Read);
  }

  public FileStream CreateFile(FullPath path) {
    ArgumentNullException.ThrowIfNull(path);
    return new FileStream(path.GetFullName(_fullNameBufferPool), FileMode.CreateNew, FileAccess.ReadWrite,
      FileShare.None);
  }

  public void CreateFileSymbolicLink(FullPath path, string target) {
    ArgumentNullException.ThrowIfNull(path);
    File.CreateSymbolicLink(path.GetFullName(_fullNameBufferPool), target);
  }

  public void CreateDirectorySymbolicLink(FullPath path, string target) {
    ArgumentNullException.ThrowIfNull(path);
    Directory.CreateSymbolicLink(path.GetFullName(_fullNameBufferPool), target);
  }

  public void CreateJunctionPoint(FullPath path, string target) {
    ArgumentNullException.ThrowIfNull(path);
    var targetPath = PathHelpers.IsPathAbsolute(target)
      ? target
      : path.Parent?.Combine(target).GetFullName(_fullNameBufferPool);
    targetPath = PathHelpers.NormalizePath(targetPath);
    Directory.CreateSymbolicLink(path.GetFullName(_fullNameBufferPool), targetPath);
  }

  private void CopyDirectoryReparsePoint(FullPath sourcePath, FullPath destinationPath) {
    var info = GetReparsePointInfo(sourcePath);

    if (info.IsSymbolicLink) {
      Directory.CreateSymbolicLink(destinationPath.GetFullName(_fullNameBufferPool), info.Target);
      try {
        Directory.SetLastWriteTimeUtc(destinationPath.GetFullName(_fullNameBufferPool), info.LastWriteTimeUtc);
      }
      catch {
        // Best effort
      }
    }
    else {
      throw new NotSupportedException(
        $"Error copying reparse point \"{sourcePath.GetFullName(_fullNameBufferPool)}\" (unsupported reparse point type?)");
    }
  }

  private void CopyFileReparsePoint(FullPath sourcePath, FullPath destinationPath) {
    var info = GetReparsePointInfo(sourcePath);

    if (info.IsSymbolicLink) {
      File.CreateSymbolicLink(destinationPath.GetFullName(_fullNameBufferPool), info.Target);
      try {
        File.SetLastWriteTimeUtc(destinationPath.GetFullName(_fullNameBufferPool), info.LastWriteTimeUtc);
      }
      catch {
        // Best effort
      }
    }
    else {
      throw new NotSupportedException(
        $"Error copying reparse point \"{sourcePath.GetFullName(_fullNameBufferPool)}\" (unsupported reparse point type?)");
    }
  }

  private void RemoveAccessDeniedAttributes(FileSystemEntry entry) {
    if (entry.IsReadOnly || entry.IsSystem) {
      try {
        var attrs = entry.FileAttributes & ~(FileAttributes.ReadOnly | FileAttributes.System);
        File.SetAttributes(entry.Path.GetFullName(_fullNameBufferPool), attrs);
      }
      catch {
        // Best effort
      }
    }
  }
}
