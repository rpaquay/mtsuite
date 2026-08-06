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

using System;
using System.ComponentModel;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtsuite.CoreFileSystem;

namespace tests.FileSystemHelpers;

public class FileSystemSetup : IDisposable {
  private readonly IFileSystem _fileSystem;
  private readonly Lazy<DirectorySetup> _root;

  public FileSystemSetup() {
    _fileSystem = mtsuite.CoreFileSystem.FileSystem.Default;
    _root = new Lazy<DirectorySetup>(CreateRootDirectory);
  }

  public bool UseLongPaths { get; set; }

  public DirectorySetup Root {
    get { return _root.Value; }
  }

  public virtual IFileSystem FileSystem {
    get { return _fileSystem; }
  }

  public void Dispose() {
    if (_root.IsValueCreated) {
      var path = _root.Value.Path;
      if (_root.Value.Exists()) {
        Console.WriteLine();
        Console.WriteLine("CLEANUP: Deleting root folder of test file system: \"{0}\"", path.FullName);
        DeleteDirectoryEntriesRecurse(path);
        _fileSystem.DeleteEntry(_fileSystem.GetEntry(path));
      }
    }
  }

  public void SetReadOnlyAttribute(FileEntrySetup entry) {
    SetAttributes(entry, entry.FileAttributes | FileAttributes.ReadOnly);
  }

  public void SetSystemAttribute(FileEntrySetup entry) {
    SetAttributes(entry, entry.FileAttributes | FileAttributes.System);
  }

  public void SetAttributes(FileEntrySetup entry, FileAttributes attributes) {
    File.SetAttributes(entry.Path.FullName, attributes);
  }

  private void DeleteDirectoryEntriesRecurse(FullPath directory) {
    using (var entries = _fileSystem.GetDirectoryFiles(directory)) {
      foreach (var entry in entries.Item) {
        if (entry.IsDirectory && !entry.IsReparsePoint) {
          DeleteDirectoryEntriesRecurse(entry.Path);
        }

        _fileSystem.DeleteEntry(entry);
      }
    }
  }

  public bool SupportsSymbolicLinkCreation() {
    FileEntrySetup f2;

    try {
      f2 = Root.CreateFileLink("b", "a");
    }
    catch (Exception e) {
      if (e is UnauthorizedAccessException || (e is Win32Exception w && w.NativeErrorCode == 1314 /* ERROR_PRIVILEGE_NOT_HELD */))
        return false;
      throw;
    }

    f2.Delete();
    return true;
  }

  private DirectorySetup CreateRootDirectory() {
    var result = new DirectorySetup(this, CreateTemporaryFolder());
    Console.WriteLine("SETUP: Created temporary root folder for test file system: \"{0}\"", result.Path.FullName);
    Console.WriteLine();
    return result;
  }

  private FullPath CreateTemporaryFolder() {
    var temporaryPath = Path.GetTempPath();
    if (UseLongPaths) {
      temporaryPath = PathHelpers.MakeLongPath(temporaryPath);
    }

    var tempPath = new FullPath(temporaryPath);
    // Note: This is not 100% safe due to possible race conditions.
    for (var i = 0; i < 100; i++) {
      var folderPath = tempPath.Combine(Path.GetRandomFileName());
      FileSystemEntry entry;
      if (TryGetEntry(folderPath, out entry))
        continue;

      try {
        _fileSystem.CreateDirectory(folderPath);
        return folderPath;
      }
      catch {
        // Try again!
      }
    }

    throw new ApplicationException("Error creating temporary folder: too many tries");
  }

  public bool TryGetEntry(FullPath path, out FileSystemEntry entry) {
    try {
      entry = FileSystem.GetEntry(path);
      return true;
    }
    catch {
      entry = default(FileSystemEntry);
      return false;
    }
  }

  public FileStream CreateFile(FullPath path) {
    return File.Create(path.FullName);
  }

  public void CreateDirectory(FullPath path) {
    Directory.CreateDirectory(path.FullName);
  }

  public void CreateFileSymbolicLink(FullPath path, string target) {
    File.CreateSymbolicLink(path.FullName, target);
  }

  public void CreateDirectorySymbolicLink(FullPath path, string target) {
    Directory.CreateSymbolicLink(path.FullName, target);
  }

  public void CreateJunctionPoint(FullPath path, string target) {
    try {
      throw new PlatformNotSupportedException("Junction point not supported on this platform");
    }
    catch (PlatformNotSupportedException e) {
      Assert.Inconclusive(e.Message);
    }
  }
}
