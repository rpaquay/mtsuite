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
using mtmir;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.shared.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class MtMirrorTest {
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
    public void MtMirrorShouldThrowWithNonExistingFolder() {
      var mtmirror = new MtMirror(_sourcefs.FileSystem, _poolFactory);
      mtmirror.DoMirror(_sourcefs.Root.Path.Combine("fake"), _destfs.Root.Path, _fileComparer);
    }

    [TestMethod]
    public void MtMirrorShouldWorkWithEmptyFolder() {
      var mtmirror = new MtMirror(_sourcefs.FileSystem, _poolFactory);

      var stats = mtmirror.DoMirror(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(0, stats.DirectoryDeletedCount);
      Assert.AreEqual(0, stats.DirectoryCreatedCount);
      Assert.AreEqual(0, stats.EntryCopiedCount);
      Assert.AreEqual(0, stats.FileCopiedTotalSize);
    }

    [TestMethod]
    public void MtMirrorShouldWorkWithFiles() {
      _sourcefs.Root.CreateFile("a", 10);
      _sourcefs.Root.CreateFile("b", 11);
      _sourcefs.Root.CreateFile("c", 12);

      var mtmirror = new MtMirror(_sourcefs.FileSystem, _poolFactory);

      var stats = mtmirror.DoMirror(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(0, stats.DirectoryDeletedCount);
      Assert.AreEqual(0, stats.FileDeletedCount);
      Assert.AreEqual(0, stats.DirectoryCreatedCount);
      Assert.AreEqual(3, stats.FileCopiedCount + stats.FileClonedCount);
      Assert.AreEqual(33, stats.FileCopiedTotalSize + stats.FileClonedTotalSize);
    }

    [TestMethod]
    public void MtMirrorShouldWorkWithDirectories() {
      var dir1 = _sourcefs.Root.CreateDirectory("a");
      dir1.CreateFile("a", 10);
      dir1.CreateFile("b", 11);
      dir1.CreateFile("c", 12);
      var dir2 = _sourcefs.Root.CreateDirectory("b");
      dir2.CreateFile("b", 11);
      dir2.CreateFile("c", 12);

      var mtmirror = new MtMirror(_sourcefs.FileSystem, _poolFactory);

      var stats = mtmirror.DoMirror(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(0, stats.DirectoryDeletedCount);
      Assert.AreEqual(0, stats.FileDeletedCount);
      Assert.AreEqual(2, stats.DirectoryCreatedCount);
      Assert.AreEqual(5, stats.FileCopiedCount + stats.FileClonedCount);
      Assert.AreEqual(56, stats.FileCopiedTotalSize + stats.FileClonedTotalSize);
    }

    [TestMethod]
    public void MtMirrorShouldWorkWithNestedDirectories() {
      _sourcefs.Root.CreateDirectory("a").CreateDirectory("b").CreateDirectory("c").CreateDirectory("d");

      var mtmirror = new MtMirror(_sourcefs.FileSystem, _poolFactory);

      var stats = mtmirror.DoMirror(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(4, stats.DirectoryCreatedCount);
      Assert.AreEqual(0, stats.FileCopiedTotalSize + stats.FileClonedTotalSize);
    }

    [TestMethod]
    public void MtMirrorShouldDeleteMismatchedFiles() {
      var dir1 = _sourcefs.Root.CreateDirectory("a");
      dir1.CreateFile("a", 10);
      dir1.CreateFile("b", 11);
      dir1.CreateFile("c", 12);
      var dir2 = _sourcefs.Root.CreateDirectory("b");
      dir2.CreateFile("b", 11);
      dir2.CreateFile("c", 12);

      _destfs.Root.CreateFile("a", 10);

      var mtmirror = new MtMirror(_sourcefs.FileSystem, _poolFactory);

      var stats = mtmirror.DoMirror(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);
      foreach (var error in stats.Errors) {
        Console.WriteLine(error.Message);
      }
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(0, stats.DirectoryDeletedCount);
      Assert.AreEqual(1, stats.FileDeletedCount);
      Assert.AreEqual(10, stats.FileDeletedTotalSize);
      Assert.AreEqual(2, stats.DirectoryCreatedCount);
      Assert.AreEqual(5, stats.FileCopiedCount + stats.FileClonedCount);
      Assert.AreEqual(56, stats.FileCopiedTotalSize + stats.FileClonedTotalSize);
      Assert.AreEqual(0, stats.Errors.Count);
    }

    [TestMethod]
    public void MtMirrorShouldDeleteExtraFiles() {
      var dir1 = _sourcefs.Root.CreateDirectory("a");
      dir1.CreateFile("a", 10);
      dir1.CreateFile("b", 11);
      dir1.CreateFile("c", 12);
      var dir2 = _sourcefs.Root.CreateDirectory("b");
      dir2.CreateFile("b", 11);
      dir2.CreateFile("c", 12);

      _destfs.Root.CreateFile("f", 10);

      var mtmirror = new MtMirror(_sourcefs.FileSystem, _poolFactory);

      var stats = mtmirror.DoMirror(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(0, stats.DirectoryDeletedCount);
      Assert.AreEqual(1, stats.FileDeletedCount);
      Assert.AreEqual(10, stats.FileDeletedTotalSize);
      Assert.AreEqual(2, stats.DirectoryCreatedCount);
      Assert.AreEqual(5, stats.FileCopiedCount + stats.FileClonedCount);
      Assert.AreEqual(56, stats.FileCopiedTotalSize + stats.FileClonedTotalSize);
      Assert.AreEqual(0, stats.Errors.Count);
    }

    [TestMethod]
    public void MtMirrorShouldDeleteExtraEntries() {
      var dir1 = _sourcefs.Root.CreateDirectory("a");
      dir1.CreateFile("a", 10);
      dir1.CreateFile("b", 11);
      dir1.CreateFile("c", 12);
      var dir2 = _sourcefs.Root.CreateDirectory("b");
      dir2.CreateFile("b", 11);
      dir2.CreateFile("c", 12);

      _destfs.Root.CreateFile("f", 10);
      var ddir1 = _destfs.Root.CreateDirectory("g");
      ddir1.CreateDirectory("a");
      ddir1.CreateFile("f", 10);
      var ddir2 = _destfs.Root.CreateDirectory("c");
      ddir2.CreateFile("a", 10);

      var mtmirror = new MtMirror(_sourcefs.FileSystem, _poolFactory);

      var stats = mtmirror.DoMirror(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(2, _destfs.Root.GetEntries().Count);
      Assert.AreEqual(3, _destfs.Root.GetDirectory("a").GetEntries().Count);
      Assert.AreEqual(2, _destfs.Root.GetDirectory("b").GetEntries().Count);
      Assert.AreEqual(3, stats.DirectoryDeletedCount);
      Assert.AreEqual(3, stats.FileDeletedCount);
      Assert.AreEqual(30, stats.FileDeletedTotalSize);
      Assert.AreEqual(2, stats.DirectoryCreatedCount);
      Assert.AreEqual(5, stats.FileCopiedCount + stats.FileClonedCount);
      Assert.AreEqual(56, stats.FileCopiedTotalSize + stats.FileClonedTotalSize);
      Assert.AreEqual(0, stats.Errors.Count);
    }

    [TestMethod]
    public void MtMirrorShouldWorkWithSymbolicLinks() {
      if (!_sourcefs.SupportsSymbolicLinkCreation()) {
        Assert.Inconclusive("Symbolic links are not supported. Try running test (or Visual Studio) as Administrator.");
      }
      var dir1 = _sourcefs.Root.CreateDirectory("a");
      dir1.CreateFile("file", 10);
      dir1.CreateFileLink("fl", "a");
      dir1.CreateDirectoryLink("dl", "..");

      var mtcopy = new MtMirror(_sourcefs.FileSystem, _poolFactory);

      var stats = mtcopy.DoMirror(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual("a", _destfs.Root.GetDirectory("a").Path.Name);
      Assert.AreEqual("file", _destfs.Root.GetDirectory("a").GetFile("file").Path.Name);
      Assert.AreEqual("fl", _destfs.Root.GetDirectory("a").GetFileLink("fl").Path.Name);
      Assert.AreEqual("a", _destfs.Root.GetDirectory("a").GetFileLink("fl").Target);
      Assert.AreEqual("..", _destfs.Root.GetDirectory("a").GetDirectoryLink("dl").Target);
      Assert.AreEqual(0, stats.DirectoryDeletedCount);
      Assert.AreEqual(0, stats.FileDeletedCount);
      Assert.AreEqual(1, stats.DirectoryCreatedCount);
      Assert.AreEqual(1, stats.FileCopiedCount + stats.FileClonedCount);
      Assert.AreEqual(10, stats.FileCopiedTotalSize + stats.FileClonedTotalSize);
      Assert.AreEqual(2, stats.SymlinkCopiedCount);
      Assert.AreEqual(0, stats.Errors.Count);
    }

    [TestMethod]
    public void MtMirrorShouldPreserveFileModificationTime() {
      // Prepare
      var sourceFile = _sourcefs.Root.CreateFile("timestamp.txt", 50);
      var expectedTime = new DateTime(2022, 5, 15, 10, 30, 0, DateTimeKind.Utc);
      File.SetLastWriteTimeUtc(sourceFile.Path.FullName, expectedTime);

      // Act
      var mtmirror = new MtMirror(_sourcefs.FileSystem, _poolFactory);
      var stats = mtmirror.DoMirror(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer);

      // Assert
      Assert.AreEqual(1, stats.FileCopiedCount + stats.FileClonedCount);
      Assert.AreEqual(50, stats.FileCopiedTotalSize + stats.FileClonedTotalSize);
      Assert.AreEqual(0, stats.Errors.Count);
      var destFilePath = _destfs.Root.Path.Combine("timestamp.txt").FullName;
      Assert.IsTrue(File.Exists(destFilePath));
      var actualTime = File.GetLastWriteTimeUtc(destFilePath);
      Assert.IsTrue(Math.Abs((expectedTime - actualTime).TotalSeconds) < 2,
        $"Expected time {expectedTime} but got {actualTime}");
    }
  }
}