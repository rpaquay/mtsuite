// Copyright 2015 Renaud Paquay All Rights Reserved.
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

using mtinfo;
using mtsuite.shared.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class MtInfoTest {
    private FileSystemSetup _fileSystemSetup;

    [TestInitialize]
    public void Setup() {
      _fileSystemSetup = new FileSystemSetup();
    }

    [TestCleanup]
    public void Cleanup() {
      _fileSystemSetup.Dispose();
      _fileSystemSetup = null;
    }

    [TestMethod]
    [ExpectedException(typeof(CommandLineReturnValueException))]
    public void MtInfoShouldThrowWithNonExistingFolder() {
      var mtinfo = new MtInfo(_fileSystemSetup.FileSystem);
      mtinfo.DoCollect(_fileSystemSetup.Root.Path.Combine("fake"), new MtInfo.CollectOptions { LevelCount = 3});
    }

    [TestMethod]
    public void MtInfoShouldWorkWithEmptyFolder() {
      var mtinfo = new MtInfo(_fileSystemSetup.FileSystem);
      var summary = mtinfo.DoCollect(_fileSystemSetup.Root.Path, new MtInfo.CollectOptions { LevelCount = 3}).Summary;
      Assert.AreEqual(0, summary.Stats.DirectoryCount);
      Assert.AreEqual(0, summary.Stats.FileCount);
      Assert.AreEqual(0, summary.Stats.FileBytesTotal);
    }

    [TestMethod]
    public void MtInfoShouldWorkWithFiles() {
      _fileSystemSetup.Root.CreateFile("a", 10);
      _fileSystemSetup.Root.CreateFile("b", 11);
      _fileSystemSetup.Root.CreateFile("c", 12);
      var mtinfo = new MtInfo(_fileSystemSetup.FileSystem);

      var summary = mtinfo.DoCollect(_fileSystemSetup.Root.Path, new MtInfo.CollectOptions { LevelCount = 3 }).Summary;
      Assert.AreEqual(0, summary.Stats.DirectoryCount);
      Assert.AreEqual(3, summary.Stats.FileCount);
      Assert.AreEqual(33, summary.Stats.FileBytesTotal);
    }

    [TestMethod]
    public void MtInfoShouldWorkWithDirectories() {
      var dir1 = _fileSystemSetup.Root.CreateDirectory("a");
      dir1.CreateFile("a", 10);
      dir1.CreateFile("b", 11);
      dir1.CreateFile("c", 12);
      var dir2 = _fileSystemSetup.Root.CreateDirectory("b");
      dir2.CreateFile("b", 11);
      dir2.CreateFile("c", 12);
      var mtinfo = new MtInfo(_fileSystemSetup.FileSystem);

      var summary = mtinfo.DoCollect(_fileSystemSetup.Root.Path, new MtInfo.CollectOptions { LevelCount = 3 }).Summary;
      Assert.AreEqual(2, summary.Stats.DirectoryCount);
      Assert.AreEqual(5, summary.Stats.FileCount);
      Assert.AreEqual(56, summary.Stats.FileBytesTotal);
    }

    [TestMethod]
    public void MtInfoShouldWorkWithNestedDirectories() {
      _fileSystemSetup.Root.CreateDirectory("a").CreateDirectory("b").CreateDirectory("c").CreateDirectory("d");
      var mtinfo = new MtInfo(_fileSystemSetup.FileSystem);

      var summary = mtinfo.DoCollect(_fileSystemSetup.Root.Path, new MtInfo.CollectOptions { LevelCount = 3 }).Summary;
      Assert.AreEqual(4, summary.Stats.DirectoryCount);
      Assert.AreEqual(0, summary.Stats.FileCount);
      Assert.AreEqual(0, summary.Stats.FileBytesTotal);
    }

    [TestMethod]
    public void MtInfoShouldWorkWithLinks() {
      if (!_fileSystemSetup.SupportsSymbolicLinkCreation()) {
        Assert.Inconclusive("Symbolic links are not supported. Try running test (or Visual Studio) as Administrator.");
      }
      var dir = _fileSystemSetup.Root.CreateDirectory("a");
      dir.CreateFile("a", 10);
      dir.CreateFileLink("b", "a");

      var mtinfo = new MtInfo(_fileSystemSetup.FileSystem);

      var summary = mtinfo.DoCollect(_fileSystemSetup.Root.Path, new MtInfo.CollectOptions { LevelCount = 3 }).Summary;
      Assert.AreEqual(1, summary.Stats.DirectoryCount);
      Assert.AreEqual(1, summary.Stats.FileCount);
      Assert.AreEqual(1, summary.Stats.SymlinkCount);
      Assert.AreEqual(10, summary.Stats.FileBytesTotal);
    }
  }
}