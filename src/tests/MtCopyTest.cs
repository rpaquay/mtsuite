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

using mtcopy;
using mtsuite.shared.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class MtCopyTest {
    private FileSystemSetup _sourcefs;
    private FileSystemSetup _destfs;

    [TestInitialize]
    public void Setup() {
      _sourcefs = new FileSystemSetup();
      _destfs = new FileSystemSetup();
    }

    [TestCleanup]
    public void Cleanup() {
      _sourcefs.Dispose();
      _sourcefs = null;
      _destfs.Dispose();
      _destfs = null;
    }

    [TestMethod]
    [ExpectedException(typeof(CommandLineReturnValueException))]
    public void MtCopyShouldThrowWithNonExistingFolder() {
      var mtcopy = new MtCopy(_sourcefs.FileSystem);
      mtcopy.DoCopy(_sourcefs.Root.Path.Combine("fake"), _destfs.Root.Path);
    }

    [TestMethod]
    public void MtCopyShouldWorkWithEmptyFolder() {
      var mtcopy = new MtCopy(_sourcefs.FileSystem);

      var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(0, stats.DirectoryDeletedCount);
      Assert.AreEqual(0, stats.DirectoryCreatedCount);
      Assert.AreEqual(0, stats.EntryCopiedCount);
      Assert.AreEqual(0, stats.Errors.Count);
    }

    [TestMethod]
    public void MtCopyShouldWorkWithFiles() {
      _sourcefs.Root.CreateFile("a", 10);
      _sourcefs.Root.CreateFile("b", 11);
      _sourcefs.Root.CreateFile("c", 12);

      var mtcopy = new MtCopy(_sourcefs.FileSystem);

      var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path);
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
      var dir1 = _sourcefs.Root.CreateDirectory("a");
      dir1.CreateFile("a", 10);
      dir1.CreateFile("b", 11);
      dir1.CreateFile("c", 12);
      var dir2 = _sourcefs.Root.CreateDirectory("b");
      dir2.CreateFile("b", 11);
      dir2.CreateFile("c", 12);

      var mtcopy = new MtCopy(_sourcefs.FileSystem);

      var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path);
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
      _sourcefs.Root.CreateDirectory("a").CreateDirectory("b").CreateDirectory("c").CreateDirectory("d");

      var mtcopy = new MtCopy(_sourcefs.FileSystem);

      var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(4, stats.DirectoryCreatedCount);
      Assert.AreEqual(0, stats.Errors.Count);
    }

    [TestMethod]
    public void MtCopyShouldDeleteMismatchedFile() {
      var dir1 = _sourcefs.Root.CreateDirectory("a");
      dir1.CreateFile("a", 10);
      dir1.CreateFile("b", 11);
      dir1.CreateFile("c", 12);
      var dir2 = _sourcefs.Root.CreateDirectory("b");
      dir2.CreateFile("b", 11);
      dir2.CreateFile("c", 12);

      _destfs.Root.CreateFile("a", 10);

      var mtcopy = new MtCopy(_sourcefs.FileSystem);

      var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual(0, stats.DirectoryDeletedCount);
      Assert.AreEqual(1, stats.FileDeletedCount);
      Assert.AreEqual(2, stats.DirectoryCreatedCount);
      Assert.AreEqual(5, stats.FileCopiedCount);
      Assert.AreEqual(0, stats.Errors.Count);
    }

    [TestMethod]
    public void MtCopyShouldNotDeleteExtraEntries() {
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

      var mtmirror = new MtCopy(_sourcefs.FileSystem);

      var stats = mtmirror.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path);
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
      if (!_sourcefs.SupportsSymbolicLinkCreation()) {
        Assert.Inconclusive("Symbolic links are not supported. Try running test (or Visual Studio) as Administrator.");
      }
      var dir1 = _sourcefs.Root.CreateDirectory("a");
      dir1.CreateFile("a", 10);
      dir1.CreateFileLink("b", "a");
      dir1.CreateDirectoryLink("c", "..");

      var mtcopy = new MtCopy(_sourcefs.FileSystem);

      var stats = mtcopy.DoCopy(_sourcefs.Root.Path, _destfs.Root.Path);
      Assert.IsTrue(_sourcefs.Root.Exists());
      Assert.IsTrue(_destfs.Root.Exists());
      Assert.AreEqual("a", _destfs.Root.GetDirectory("a").Path.Name);
      Assert.AreEqual("a", _destfs.Root.GetDirectory("a").GetFile("a").Path.Name);
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
  }
}