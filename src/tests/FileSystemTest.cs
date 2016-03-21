// Copyright 2016 Renaud Paquay All Rights Reserved.
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

using Microsoft.VisualStudio.TestTools.UnitTesting;
using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class FileSystemTest {
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
    public void CreateJunctionPointWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");

      // Act
      var junctionPoint = _fileSystemSetup.Root.CreateJunctionPoint("jct", "foo");

      // Assert
    }

    [TestMethod]
    public void CreatedJunctionPointRedirectionWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");
      var testFile = fooTarget.CreateFile("testfile.txt", 200);

      // Act
      var junctionPoint = _fileSystemSetup.Root.CreateJunctionPoint("jct", "foo");

      // Assert
      Assert.IsTrue(_fileSystemSetup.FileSystem.GetEntry(junctionPoint.Path.Combine("testfile.txt")).IsFile);
    }

    [TestMethod]
    public void CreatedJunctionPointToLongPathWorks() {
      _fileSystemSetup.UseLongPaths = true;

      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");
      while (fooTarget.Path.Length < 300) {
        fooTarget = fooTarget.CreateDirectory("subdir");
      }
      fooTarget.CreateFile("testfile.txt", 200);

      // Act
      var junctionPoint = _fileSystemSetup.Root.CreateJunctionPoint("jct", fooTarget.Path.Text);

      // Assert
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(junctionPoint.Path);
      Assert.IsTrue(info.IsJunctionPoint);
      Assert.IsFalse(info.IsTargetRelative);
      Assert.AreEqual(fooTarget.Path.Path, info.Target);

      Assert.IsTrue(_fileSystemSetup.FileSystem.GetEntry(junctionPoint.Path.Combine("testfile.txt")).IsFile);
    }

    [TestMethod]
    public void GetReparsePointInfoWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");

      // Act
      var junctionPoint = _fileSystemSetup.Root.CreateJunctionPoint("jct", "foo");

      // Assert
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(junctionPoint.Path);
      Assert.IsTrue(info.IsJunctionPoint);
      Assert.IsFalse(info.IsTargetRelative);
      Assert.AreEqual(fooTarget.Path.Path, info.Target);
    }

    [TestMethod]
    public void DeleteEntryForJunctionPointWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");
      fooTarget.CreateFile("testfile.txt", 20);
      var junctionPoint = _fileSystemSetup.Root.CreateJunctionPoint("jct", "foo");

      // Act
      var entry = _fileSystemSetup.FileSystem.GetEntry(junctionPoint.Path);
      _fileSystemSetup.FileSystem.DeleteEntry(entry);

      // Assert
      Assert.IsFalse(_fileSystemSetup.FileSystem.TryGetEntry(junctionPoint.Path, out entry));
    }

    [TestMethod]
    public void CopyFileForJunctionPointWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");
      fooTarget.CreateFile("testfile.txt", 20);
      var junctionPoint = _fileSystemSetup.Root.CreateJunctionPoint("jct", "foo");

      // Act
      var jct2Path = fooTarget.Path.Combine("jct-copy");
      var entry = _fileSystemSetup.FileSystem.GetEntry(junctionPoint.Path);
      _fileSystemSetup.FileSystem.CopyFile(entry, jct2Path, (a, b) => { });

      // Assert
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(jct2Path);
      Assert.IsTrue(info.IsJunctionPoint);
      Assert.IsFalse(info.IsTargetRelative);
      Assert.AreEqual(fooTarget.Path.Path, info.Target);
    }
  }
}