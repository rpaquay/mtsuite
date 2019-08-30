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

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtsuite.CoreFileSystem;
using System.Linq;
using System.Text;
using mtsuite.CoreFileSystem.Win32;
using tests.FileSystemHelpers;
using System;
using System.IO;

namespace tests {
  [TestClass]
  public class FileSystemTest {
    private FileSystemSetup _fileSystemSetup;

    public static int RunCommand(string[] args) {
      var imageName = args[0];

      var sb = new StringBuilder();
      for (var i = 1; i < args.Length; i++) {
        var arg = args[i];

        if (arg.Contains(" ")) {
          sb.AppendFormat("\"{0}\"", arg);
        } else {
          sb.Append(arg);
        }

        if (i < args.Length - 1) {
          sb.Append(" ");
        }
      }

      var startInfo = new System.Diagnostics.ProcessStartInfo();
      startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
      startInfo.FileName = imageName;
      startInfo.Arguments = sb.ToString();

      var process = new System.Diagnostics.Process();
      process.StartInfo = startInfo;
      process.Start();
      process.WaitForExit();

      return process.ExitCode;
    }

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
    public void CreateFileSymbolicLinkWorks() {
      if (!_fileSystemSetup.SupportsSymbolicLinkCreation()) {
        Assert.Inconclusive("Symbolic links are not supported. Try running test (or Visual Studio) as Administrator.");
      }

      // Prepare
      _fileSystemSetup.Root.CreateFile("foo.txt", 100);

      // Act
      var link = _fileSystemSetup.Root.CreateFileLink("link.txt", "foo.txt");

      // Assert
      Assert.IsTrue(_fileSystemSetup.FileSystem.GetEntry(link.Path).IsReparsePoint);
    }

    [TestMethod]
    public void GetJunctionPointInfoWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo with spaces");

      // Act
      var junctionPointPath = fooTarget.Parent.Path.Combine("foo.junction");
      int rc = RunCommand(new[] { "cmd.exe", "/c", "mklink", "/j", junctionPointPath.FullName, fooTarget.Path.FullName });
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(junctionPointPath);

      // Assert
      Assert.AreEqual(0, rc);
      Assert.IsTrue(info.IsJunctionPoint);
      Assert.AreEqual(fooTarget.Path.FullName, info.Target);
    }

    [TestMethod]
    public void GetDirectorySymbolicLinkInfoWorks() {
      if (!_fileSystemSetup.SupportsSymbolicLinkCreation()) {
        Assert.Inconclusive("Symbolic links are not supported. Try running test (or Visual Studio) as Administrator.");
      }

      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo with spaces");

      // Act
      var junctionPointPath = fooTarget.Parent.Path.Combine("foo.junction");
      int rc = RunCommand(new[] { "cmd.exe", "/c", "mklink", "/d", junctionPointPath.FullName, fooTarget.Path.FullName });
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(junctionPointPath);

      // Assert
      Assert.AreEqual(0, rc);
      Assert.IsTrue(info.IsSymbolicLink);
      Assert.AreEqual(fooTarget.Path.FullName, info.Target);
    }

    [TestMethod]
    public void GetFileSymbolicLinkInfoWorks() {
      if (!_fileSystemSetup.SupportsSymbolicLinkCreation()) {
        Assert.Inconclusive("Symbolic links are not supported. Try running test (or Visual Studio) as Administrator.");
      }

      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateFile("foo with spaces", 200);

      // Act
      var junctionPointPath = fooTarget.Parent.Path.Combine("foo.junction");
      int rc = RunCommand(new[] { "cmd.exe", "/c", "mklink", junctionPointPath.FullName, fooTarget.Path.FullName });
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(junctionPointPath);

      // Assert
      Assert.AreEqual(0, rc);
      Assert.IsTrue(info.IsSymbolicLink);
      Assert.AreEqual(fooTarget.Path.FullName, info.Target);
    }

    [TestMethod]
    public void CreateJunctionPointWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");

      // Act
      var junctionPoint = _fileSystemSetup.Root.CreateJunctionPoint("jct", fooTarget.Path.FullName);

      // Assert
      Assert.IsTrue(_fileSystemSetup.FileSystem.GetEntry(junctionPoint.Path).IsReparsePoint);
      Assert.IsTrue(_fileSystemSetup.FileSystem.GetReparsePointInfo(junctionPoint.Path).IsJunctionPoint);
    }

    [TestMethod]
    public void CreatedJunctionPointRedirectionWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");
      fooTarget.CreateFile("testfile.txt", 200);

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
      var junctionPoint = _fileSystemSetup.Root.CreateJunctionPoint("jct", fooTarget.Path.FullName);

      // Assert
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(junctionPoint.Path);
      Assert.IsTrue(info.IsJunctionPoint);
      Assert.IsFalse(info.IsTargetRelative);
      Assert.AreEqual(fooTarget.Path.FullName, info.Target);

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
      Assert.AreEqual(fooTarget.Path.FullName, info.Target);
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
      _fileSystemSetup.FileSystem.CopyFile(entry, jct2Path, CopyFileOptions.Default, (a, b) => { });

      // Assert
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(jct2Path);
      Assert.IsTrue(info.IsJunctionPoint);
      Assert.IsFalse(info.IsTargetRelative);
      Assert.AreEqual(fooTarget.Path.FullName, info.Target);
    }

    [TestMethod]
    public void EnumerateDirectoryEntriesWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");
      fooTarget.CreateFile("testfile.txt", 20);

      // Act
      var entries = _fileSystemSetup.FileSystem.EnumerateDirectoryEntries(fooTarget.Path).ToList();

      // Assert
      Assert.AreEqual(1, entries.Count);
      Assert.AreEqual("testfile.txt", entries[0].Name);
    }

    [TestMethod]
    public void GetDirectoryEntriesEnumeratorWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");
      fooTarget.CreateFile("testfile.txt", 20);
      fooTarget.CreateDirectory("dir1");

      // Act
      var entries = new List<DirectoryEntry>();
      using (var e = _fileSystemSetup.FileSystem.GetDirectoryEntriesEnumerator(fooTarget.Path, null)) {
        while (e.MoveNext()) {
          entries.Add(e.Current);
        }
      }

      // Assert
      Assert.AreEqual(2, entries.Count);
      Assert.IsNotNull(entries.FirstOrDefault(x => x.FileName == "testfile.txt"));
      Assert.IsNotNull(entries.FirstOrDefault(x => x.FileName == "dir1"));
    }

    [TestMethod]
    public void GetDirectoryFilesEnumeratorWorks() {
      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");
      fooTarget.CreateFile("testfile.txt", 20);
      fooTarget.CreateDirectory("dir1");

      // Act
      var entries = new List<DirectoryEntry>();
      using (var e = _fileSystemSetup.FileSystem.GetDirectoryEntriesEnumerator(fooTarget.Path, null)) {
        while (e.MoveNext()) {
          entries.Add(e.Current);
        }
      }

      // Assert
      Assert.AreEqual(2, entries.Count);

      var fileData = entries.First(x => x.FileName == "testfile.txt");
      var fileEntry = new FileSystemEntryData(fileData.Data);
      Assert.IsTrue(fileEntry.IsFile);

      var dirData = entries.First(x => x.FileName == "dir1");
      var dirEntry = new FileSystemEntryData(dirData.Data);
      Assert.IsTrue(dirEntry.IsDirectory);
    }

    [TestMethod]
    public void GetDirectoryFilesEnumeratorWorksWithLotsOfFiles() {
      // Prepare
      const int fileCount = 1024; // This should be enough to ensure multiple calls to NtQueryDirectoryFile
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo");
      for (var i = 0; i < fileCount; i++) {
        fooTarget.CreateFile(String.Format("testfile{0:00000}.txt", i), 20 + i);
      }

      // Act
      var entries = new List<FileIdFullInformation>();
      using (var e = _fileSystemSetup.FileSystem.GetDirectoryFilesEnumerator(fooTarget.Path, null)) {
        while (e.MoveNext()) {
          entries.Add(e.Current);
        }
      }

      // Assert
      Assert.AreEqual(fileCount, entries.Count);

      for (var i = 0; i < fileCount; i++) {
        Assert.AreEqual(String.Format("testfile{0:00000}.txt", i), entries[i].FileName);
        Assert.AreEqual(20L + i, entries[i].FileSize);
        Assert.IsTrue(IsTodaysDate(entries[i].CreationTimeUtc));
        Assert.IsTrue(IsTodaysDate(entries[i].LastAccessTimeUtc));
        Assert.IsTrue(IsTodaysDate(entries[i].LastWriteTimeUtc));
        Assert.AreEqual(FileAttributes.Archive, entries[i].Attributes);
      }
    }

    [TestMethod]
    public void GetDirectoryFilesEnumeratorWorksWithSymbolicLinks() {
      if (!_fileSystemSetup.SupportsSymbolicLinkCreation()) {
        Assert.Inconclusive("Symbolic links are not supported. Try running test (or Visual Studio) as Administrator.");
      }

      // Prepare
      _fileSystemSetup.Root.CreateFile("foo.txt", 100);

      // Act
      var link = _fileSystemSetup.Root.CreateFileLink("link.txt", "foo.txt");
      var entries = new List<FileIdFullInformation>();
      using (var e = _fileSystemSetup.FileSystem.GetDirectoryFilesEnumerator(_fileSystemSetup.Root.Path, null)) {
        while (e.MoveNext()) {
          entries.Add(e.Current);
        }
      }

      // Assert
      Assert.IsTrue(entries.Count == 2);
      Assert.IsTrue(new FileSystemEntryData(entries[0]).IsFile);
      Assert.IsFalse(new FileSystemEntryData(entries[0]).IsReparsePoint);
      Assert.IsTrue(IsTodaysDate(new FileSystemEntryData(entries[1]).LastWriteTimeUtc));

      Assert.IsTrue(new FileSystemEntryData(entries[1]).IsFile);
      Assert.IsTrue(new FileSystemEntryData(entries[1]).IsReparsePoint);
      Assert.IsTrue(IsTodaysDate(new FileSystemEntryData(entries[1]).LastWriteTimeUtc));
    }

    private bool IsTodaysDate(DateTime dateTimeUtc) {
      var utcNow = DateTime.UtcNow;
      return utcNow.DayOfYear == dateTimeUtc.DayOfYear;
    }
  }
}