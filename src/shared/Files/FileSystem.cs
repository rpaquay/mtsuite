﻿// Copyright 2015 Renaud Paquay All Rights Reserved.
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

using System.Collections.Generic;
using System.IO;
using mtsuite.shared.Collections;
using mtsuite.shared.Win32;

namespace mtsuite.shared.Files {
  public class FileSystem : IFileSystem {
    private readonly Win32.Win32 _win32 = new Win32.Win32();
    private readonly IPool<List<FileSystemEntry>> _entryListPool = new ListPool<FileSystemEntry>();

    public FileSystemEntry GetEntry(FullPath path) {
      var data = _win32.GetFileAttributesEx(path);
      return new FileSystemEntry(path, data);
    }

    public bool TryGetEntry(FullPath path, out FileSystemEntry entry) {
      WIN32_FILE_ATTRIBUTE_DATA data;
      if (_win32.TryGetFileAttributesEx(path, out data) != Win32Errors.ERROR_SUCCESS) {
        entry = default(FileSystemEntry);
        return false;
      }

      entry = new FileSystemEntry(path, data);
      return true;
    }

    public FromPool<List<FileSystemEntry>> GetDirectoryEntries(FullPath path) {
      using (var entries = _win32.GetDirectoryEntries(path)) {
        var result = _entryListPool.AllocateFrom();
        foreach (var x in entries.Item) {
          result.Item.Add(new FileSystemEntry(path.Combine(x.Name), x.Data));
        }
        return result;
      }
    }

    public void DeleteEntry(FileSystemEntry entry) {
      RemoveReadOnly(entry);
      var path = entry.Path;
      if (entry.IsDirectory) {
        _win32.DeleteDirectory(path);
      } else {
        _win32.DeleteFile(path);
      }
    }

    private void RemoveReadOnly(FileSystemEntry entry) {
      if (entry.IsReadOnly) {
        var attrs = entry.FileAttributes & ~FileAttributes.ReadOnly;
        _win32.SetFileAttributes(entry.Path, (FILE_ATTRIBUTE)attrs);
      }
    }

    public void CopyFile(FileSystemEntry entry, FullPath destinationPath, CopyFileCallback callback) {
      var sourcePath = entry.Path;
      if (entry.IsDirectory && entry.IsReparsePoint) {
        //TODO: Find better way to deal with this
        try {
          var destinationEntry = GetEntry(destinationPath);
          DeleteEntry(destinationEntry);
        } catch {
          // Nothing to do here, as CopyFile will report an exception below.
        }
        _win32.CopyDirectoryReparsePoint(sourcePath, destinationPath);
      } else {
        // If destination exists and is read-only, remove the read-only attribute
        try {
          var destinationEntry = GetEntry(destinationPath);
          RemoveReadOnly(destinationEntry);
        } catch {
          // Nothing to do here, as CopyFile will report an exception below.
        }

        _win32.CopyFile(sourcePath, destinationPath, callback);
      }
    }

    public FileStream OpenFile(FullPath path, FileAccess access) {
      var fileAccess = NativeMethods.EFileAccess.GenericRead;
      if ((access & FileAccess.Read) != 0)
        fileAccess = NativeMethods.EFileAccess.FILE_GENERIC_READ;
      if ((access & FileAccess.Write) != 0)
        fileAccess |= NativeMethods.EFileAccess.FILE_GENERIC_WRITE;

      var handle = _win32.OpenFile(path, fileAccess,
        NativeMethods.EFileShare.Read,
        NativeMethods.ECreationDisposition.OpenExisting, NativeMethods.EFileAttributes.Normal);
      return new FileStream(handle, access);
    }

    public FileStream CreateFile(FullPath path) {
      var handle = _win32.OpenFile(path,
        NativeMethods.EFileAccess.FILE_GENERIC_READ | NativeMethods.EFileAccess.FILE_GENERIC_WRITE,
        NativeMethods.EFileShare.None,
        NativeMethods.ECreationDisposition.CreateAlways,
        NativeMethods.EFileAttributes.Normal);
      return new FileStream(handle, FileAccess.ReadWrite);
    }

    public void CreateDirectory(FullPath path) {
      CreateDirectoryWorker(path);
    }

    public void CreateFileSymbolicLink(FullPath path, string target) {
      _win32.CreateFileSymbolicLink(path, target);
    }

    public void CreateDirectorySymbolicLink(FullPath path, string target) {
      _win32.CreateDirectorySymbolicLink(path, target);
    }

    public ReparsePointInfo GetReparsePointInfo(FullPath path) {
      var info = _win32.GetReparsePointInfo(path);
      return new ReparsePointInfo {
        IsJunctionPoint = info.IsJunctionPoint,
        IsSymbolicLink = info.IsSymbolicLink,
        Target = info.Target,
        IsTargetRelative = info.IsTargetRelative,
        CreationTimeUtc = info.CreationTimeUtc,
        LastAccessTimeUtc = info.LastAccessTimeUtc,
        LastWriteTimeUtc = info.LastWriteTimeUtc,
      };
    }

    public void CreateDirectoryWorker(FullPath path) {
      if (path == null)
        return;

      try {
        _win32.CreateDirectory(path);
      } catch {
        CreateDirectoryWorker(path.Parent);
        _win32.CreateDirectory(path);
      }
    }
  }
}