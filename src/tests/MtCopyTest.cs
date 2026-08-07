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
using System.IO;
using mtcopy;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.shared.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tests.FileSystemHelpers;

namespace tests;

[TestClass]
public class MtCopyTest {
  private FileSystemSetup _sourcefs;
  private FileSystemSetup _destfs;
  private MtPoolFactory _poolFactory;
  private IFileComparer _fileComparer;

  [TestInitialize]
  public void Setup() {
    _sourcefs = new FileSystemSetup();
    _destfs = new FileSystemSetup();
    _poolFactory = new MtPoolFactory();
    _fileComparer = new FileContentsFileComparer(_sourcefs.FileSystem, _poolFactory);
  }

  [TestCleanup]
  public void Cleanup() {
    _sourcefs.Dispose();
    _sourcefs = null;
    _destfs.Dispose();
    _destfs = null;
    _poolFactory = null;
  }

  [TestMethod]
  [ExpectedException(typeof(CommandLineReturnValueException))]
  public void MtCopyShouldThrowWithNonExistingFolder() {
    // Prepare

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    mtcopy.DoCopy(_sourcefs.Root.Path.Combine("fake"), _destfs.Root.Path, _fileComparer);

    // Assert
  }

  [TestMethod]
  public void MtCopyShouldWorkWithEmptyFolder() {
    // Prepare

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.DirectoryCreatedCount);
    Assert.AreEqual(0, stats.EntryCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldWorkWithFiles() {
    // Prepare
    _sourcefs.Root.CreateFile("a", 10);
    _sourcefs.Root.CreateFile("b", 11);
    _sourcefs.Root.CreateFile("c", 12);

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.FileDeletedCount);
    Assert.AreEqual(0, stats.DirectoryCreatedCount);
    Assert.AreEqual(3, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldWorkWithDirectories() {
    // Prepare
    var dir1 = _sourcefs.Root.CreateDirectory("a");
    dir1.CreateFile("a", 10);
    dir1.CreateFile("b", 11);
    dir1.CreateFile("c", 12);
    var dir2 = _sourcefs.Root.CreateDirectory("b");
    dir2.CreateFile("b", 11);
    dir2.CreateFile("c", 12);

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.FileDeletedCount);
    Assert.AreEqual(2, stats.DirectoryCreatedCount);
    Assert.AreEqual(5, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldWorkWithNestedDirectories() {
    // Prepare
    _sourcefs.Root.CreateDirectory("a").CreateDirectory("b").CreateDirectory("c").CreateDirectory("d");

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual(4, stats.DirectoryCreatedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldDeleteMismatchedFile() {
    // Prepare
    var dir1 = _sourcefs.Root.CreateDirectory("a");
    dir1.CreateFile("a", 10);
    dir1.CreateFile("b", 11);
    dir1.CreateFile("c", 12);
    var dir2 = _sourcefs.Root.CreateDirectory("b");
    dir2.CreateFile("b", 11);
    dir2.CreateFile("c", 12);

    _destfs.Root.CreateFile("a", 10); // "a" is a dir in sourcefs!

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(1, stats.FileDeletedCount);
    Assert.AreEqual(2, stats.DirectoryCreatedCount);
    Assert.AreEqual(5, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldDeleteReadOnlyFiles() {
    // Prepare
    _sourcefs.Root.CreateFile("a", 15);
    _destfs.Root.CreateFile("a", 10).SetReadOnlyAttribute();

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.FileDeletedCount);
    Assert.AreEqual(0, stats.DirectoryCreatedCount);
    Assert.AreEqual(1, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldDeleteSystemFiles() {
    // Prepare
    _destfs.Root.CreateFile("a", 10).SetSystemAttribute();
    _sourcefs.Root.CreateFile("a", 15);

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.FileDeletedCount);
    Assert.AreEqual(0, stats.DirectoryCreatedCount);
    Assert.AreEqual(1, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldNotDeleteExtraEntries() {
    // Prepare
    var dir1 = _sourcefs.Root.CreateDirectory("a");
    dir1.CreateFile("a", 10);
    dir1.CreateFile("b", 11);
    dir1.CreateFile("c", 12);
    var dir2 = _sourcefs.Root.CreateDirectory("b");
    dir2.CreateFile("b", 11);
    dir2.CreateFile("c", 12);
    var dir3 = _sourcefs.Root.CreateDirectory("c");

    _destfs.Root.CreateFile("f", 10);
    var ddir1 = _destfs.Root.CreateDirectory("g");
    ddir1.CreateDirectory("a");
    ddir1.CreateFile("f", 10);
    var ddir2 = _destfs.Root.CreateDirectory("c");
    ddir2.CreateFile("a", 10);

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual("f", _destfs.Root.GetFile("f").Path.Name);
    Assert.AreEqual("c", _destfs.Root.GetDirectory("c").Path.Name);
    Assert.AreEqual("a", _destfs.Root.GetDirectory("c").GetFile("a").Path.Name);
    Assert.AreEqual("g", _destfs.Root.GetDirectory("g").Path.Name);
    Assert.AreEqual("a", _destfs.Root.GetDirectory("g").GetDirectory("a").Path.Name);
    Assert.AreEqual("f", _destfs.Root.GetDirectory("g").GetFile("f").Path.Name);
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.FileDeletedCount);
    Assert.AreEqual(2, stats.DirectoryCreatedCount);
    Assert.AreEqual(5, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldWorkWithSymbolicLinks() {
    // Prepare
    if (!_sourcefs.SupportsSymbolicLinkCreation()) {
      Assert.Inconclusive("Symbolic links are not supported. Try running test (or Visual Studio) as Administrator.");
    }
    var dir1 = _sourcefs.Root.CreateDirectory("a");
    dir1.CreateFile("a", 10);
    dir1.CreateFileLink("b", "a");
    dir1.CreateDirectoryLink("c", "..");

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual("a", _destfs.Root.GetDirectory("a").Path.Name);
    Assert.AreEqual("a", _destfs.Root.GetDirectory("a").GetFile("a").Path.Name);
    Assert.IsTrue(_destfs.Root.GetEntry("a").IsDirectory);
    Assert.IsTrue(_destfs.Root.GetDirectory("a").GetEntry("b").IsSymlink);
    Assert.IsTrue(_destfs.Root.GetDirectory("a").GetEntry("c").IsSymlink);
    Assert.AreEqual("b", _destfs.Root.GetDirectory("a").GetFileLink("b").Path.Name);
    Assert.AreEqual("a", _destfs.Root.GetDirectory("a").GetFileLink("b").Target);
    Assert.AreEqual("..", _destfs.Root.GetDirectory("a").GetDirectoryLink("c").Target);
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.FileDeletedCount);
    Assert.AreEqual(1, stats.DirectoryCreatedCount);
    Assert.AreEqual(1, stats.FileCopiedCount);
    Assert.AreEqual(2, stats.SymlinkCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldCopyJunctionPoints() {
    // Prepare
    var dir1 = _sourcefs.Root.CreateDirectory("a");
    dir1.CreateFile("f.txt", 10);
    dir1.CreateDirectory("subdir");
    dir1.CreateJunctionPoint("jct", "subdir");

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual("a", _destfs.Root.GetDirectory("a").Path.Name);
    Assert.AreEqual("f.txt", _destfs.Root.GetDirectory("a").GetFile("f.txt").Path.Name);
    // Note: Target points to "_sourcefs" because junction point targets are absolute paths.
    Assert.AreEqual(_sourcefs.Root.GetDirectory("a").GetDirectory("subdir").Path.FullName, _destfs.Root.GetDirectory("a").GetJunctionPoint("jct").Target);
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.FileDeletedCount);
    Assert.AreEqual(2, stats.DirectoryCreatedCount);
    Assert.AreEqual(1, stats.FileCopiedCount);
    Assert.AreEqual(1, stats.SymlinkCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldUpdateExistingJunctionPoints() {
    // Prepare
    var dir1 = _sourcefs.Root.CreateDirectory("a");
    dir1.CreateFile("f.txt", 10);
    dir1.CreateDirectory("subdir");
    dir1.CreateJunctionPoint("jct", "subdir");

    var dir2 = _destfs.Root.CreateDirectory("a");
    dir2.CreateDirectory("subdir");
    dir2.CreateJunctionPoint("jct", "subdir");

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual("a", _destfs.Root.GetDirectory("a").Path.Name);
    Assert.AreEqual("f.txt", _destfs.Root.GetDirectory("a").GetFile("f.txt").Path.Name);
    // Note: Target points to "_sourcefs" because junction point targets are absolute paths.
    Assert.AreEqual(_sourcefs.Root.GetDirectory("a").GetDirectory("subdir").Path.FullName, _destfs.Root.GetDirectory("a").GetJunctionPoint("jct").Target);
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.FileDeletedCount);
    Assert.AreEqual(0, stats.DirectoryCreatedCount);
    Assert.AreEqual(1, stats.FileCopiedCount);
    Assert.AreEqual(1, stats.SymlinkCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldReplaceExistingJunctionPoint() {
    // Prepare
    var dir1 = _sourcefs.Root.CreateDirectory("a");
    dir1.CreateFile("f.txt", 10);
    dir1.CreateDirectory("subdir");
    dir1.CreateFile("jct", 10);

    var dir2 = _destfs.Root.CreateDirectory("a");
    dir2.CreateDirectory("subdir");
    dir2.CreateJunctionPoint("jct", "subdir");

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual("a", _destfs.Root.GetDirectory("a").Path.Name);
    Assert.AreEqual("f.txt", _destfs.Root.GetDirectory("a").GetFile("f.txt").Path.Name);
    Assert.AreEqual("jct", _destfs.Root.GetDirectory("a").GetFile("jct").Path.Name);
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(0, stats.FileDeletedCount);
    Assert.AreEqual(1, stats.SymlinkDeletedCount);
    Assert.AreEqual(0, stats.DirectoryCreatedCount);
    Assert.AreEqual(2, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.SymlinkCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldReplaceExistingEntryWithJunctionPoint() {
    // Prepare
    var dir1 = _sourcefs.Root.CreateDirectory("a");
    dir1.CreateFile("f.txt", 10);
    dir1.CreateDirectory("subdir");
    dir1.CreateJunctionPoint("jct", "subdir");

    var dir2 = _destfs.Root.CreateDirectory("a");
    dir2.CreateDirectory("subdir");
    dir2.CreateFile("jct", 10);

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.IsTrue(_sourcefs.Root.Exists());
    Assert.IsTrue(_destfs.Root.Exists());
    Assert.AreEqual("a", _destfs.Root.GetDirectory("a").Path.Name);
    Assert.AreEqual("f.txt", _destfs.Root.GetDirectory("a").GetFile("f.txt").Path.Name);
    Assert.AreEqual("jct", _destfs.Root.GetDirectory("a").GetJunctionPoint("jct").Path.Name);
    Assert.AreEqual(_sourcefs.Root.GetDirectory("a").GetDirectory("subdir").Path.FullName, _destfs.Root.GetDirectory("a").GetJunctionPoint("jct").Target);
    Assert.AreEqual(0, stats.DirectoryDeletedCount);
    Assert.AreEqual(1, stats.FileDeletedCount);
    Assert.AreEqual(0, stats.SymlinkDeletedCount);
    Assert.AreEqual(0, stats.DirectoryCreatedCount);
    Assert.AreEqual(1, stats.FileCopiedCount);
    Assert.AreEqual(1, stats.SymlinkCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
  }

  [TestMethod]
  public void MtCopyShouldPreserveFileModificationTime() {
    // Prepare
    var sourceFile = _sourcefs.Root.CreateFile("timestamp.txt", 50);
    var expectedTime = new DateTime(2022, 5, 15, 10, 30, 0, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(sourceFile.Path.FullName, expectedTime);

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.AreEqual(1, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
    var destFilePath = _destfs.Root.Path.Combine("timestamp.txt").FullName;
    Assert.IsTrue(File.Exists(destFilePath));
    var actualTime = File.GetLastWriteTimeUtc(destFilePath);
    Assert.IsTrue(Math.Abs((expectedTime - actualTime).TotalSeconds) < 2,
      $"Expected time {expectedTime} but got {actualTime}");
  }

  [TestMethod]
  public void MtCopyShouldPreserveUnixFileModeOnNonWindows() {
    if (OperatingSystem.IsWindows()) {
      return;
    }

    // Prepare
    var sourceFile = _sourcefs.Root.CreateFile("script.sh", 20);
    var expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
    File.SetUnixFileMode(sourceFile.Path.FullName, expectedMode);

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.AreEqual(1, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
    var destFilePath = _destfs.Root.Path.Combine("script.sh").FullName;
    Assert.IsTrue(File.Exists(destFilePath));
    var actualMode = File.GetUnixFileMode(destFilePath);
    Assert.AreEqual(expectedMode, actualMode);
  }

  [TestMethod]
  public void MtCopyShouldSupportCaseSensitivityOnLinux() {
    if (!OperatingSystem.IsLinux()) {
      return;
    }

    // Prepare
    _sourcefs.Root.CreateFile("file.txt", 10);
    _sourcefs.Root.CreateFile("FILE.txt", 20);

    // Act
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

    // Assert
    Assert.AreEqual(2, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
    Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("file.txt").FullName));
    Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("FILE.txt").FullName));
  }

  [TestMethod]
  public void MtCopyShouldCopyLargeAndSmallFilesInParallel() {
    // Prepare
    _sourcefs.Root.CreateFile("small1.txt", 100);
    _sourcefs.Root.CreateFile("small2.txt", 200);
    _sourcefs.Root.CreateFile("large1.dat", 1024 * 1024); // 1 MB
    _sourcefs.Root.CreateFile("large2.dat", 2 * 1024 * 1024); // 2 MB

    var sub = _sourcefs.Root.CreateDirectory("subdir");
    sub.CreateFile("subsmall.txt", 50);
    sub.CreateFile("sublarge.dat", 1024 * 1024);

    // Act - copy with threshold lower than 1MB (e.g. 512KB) to exercise async task path
    var pfs = new mtsuite.shared.ParallelFileSystem(_sourcefs.FileSystem, _poolFactory) {
      LargeFileAsyncThreshold = 512 * 1024
    };
    var copyProgress = new mtsuite.shared.CopyProgressMonitor();
    var sourceEntry = _sourcefs.FileSystem.GetEntry(_sourcefs.Root.Path);
    var task = pfs.CopyDirectoryAsync(sourceEntry, _destfs.Root.Path, mtsuite.shared.CopyOptions.SkipIdenticalFiles, _fileComparer, true);
    pfs.WaitForTask(task);
    var stats = copyProgress.GetStatistics();

    // Assert
    Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("small1.txt").FullName));
    Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("small2.txt").FullName));
    Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("large1.dat").FullName));
    Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("large2.dat").FullName));
    Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("subdir").Combine("subsmall.txt").FullName));
    Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("subdir").Combine("sublarge.dat").FullName));
    Assert.AreEqual(100, new FileInfo(_destfs.Root.Path.Combine("small1.txt").FullName).Length);
    Assert.AreEqual(1024 * 1024, new FileInfo(_destfs.Root.Path.Combine("large1.dat").FullName).Length);
    Assert.AreEqual(2 * 1024 * 1024, new FileInfo(_destfs.Root.Path.Combine("large2.dat").FullName).Length);
  }

  [TestMethod]
  public void MtCopyShouldSupportNoCloneFlag() {
    // Prepare
    _sourcefs.Root.CreateFile("file.txt", 100);

    // Act - copy with NoClone option explicitly passed
    var mtcopy = new MtCopy(_sourcefs.FileSystem, _poolFactory);
    var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer, mtsuite.shared.CopyOptions.SkipIdenticalFiles | mtsuite.shared.CopyOptions.NoClone);

    // Assert
    Assert.AreEqual(1, stats.FileCopiedCount);
    Assert.AreEqual(0, stats.Errors.Count);
    Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("file.txt").FullName));
  }
}
