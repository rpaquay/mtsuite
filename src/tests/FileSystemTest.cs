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
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tests.FileSystemHelpers;

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
        }
        else {
          sb.Append(arg);
        }

        if (i < args.Length - 1) {
          sb.Append(" ");
        }
      }

      var startInfo = new ProcessStartInfo();
      startInfo.WindowStyle = ProcessWindowStyle.Hidden;
      startInfo.FileName = imageName;
      startInfo.Arguments = sb.ToString();

      var process = new Process();
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
      if (!OperatingSystem.IsWindows()) {
        Assert.Inconclusive("Junction points are only supported on Windows.");
        return;
      }

      // Prepare
      var fooTarget = _fileSystemSetup.Root.CreateDirectory("foo with spaces");

      // Act
      var junctionPointPath = fooTarget.Parent.Path.Combine("foo.junction");
      int rc = RunCommand(new[] { "cmd.exe", "/c", "mklink", "/j", junctionPointPath.FullName, fooTarget.Path.FullName });
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(junctionPointPath);

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
      var linkPath = fooTarget.Parent.Path.Combine("foo.link");
      _fileSystemSetup.CreateDirectorySymbolicLink(linkPath, fooTarget.Path.FullName);
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(linkPath);

      // Assert
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
      var linkPath = fooTarget.Parent.Path.Combine("foo.link");
      _fileSystemSetup.CreateFileSymbolicLink(linkPath, fooTarget.Path.FullName);
      var info = _fileSystemSetup.FileSystem.GetReparsePointInfo(linkPath);

      // Assert
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
    public void FileSystemExtensionFactoryCreatesPlatformExtension() {
      var poolFactory = new mtsuite.CoreFileSystem.ObjectPool.MtPoolFactory();
      var extension = mtsuite.CoreFileSystem.FileSystemExtension.Create(poolFactory);
      Assert.IsNotNull(extension);

      if (OperatingSystem.IsMacOS()) {
        Assert.IsInstanceOfType(extension, typeof(mtsuite.CoreFileSystem.MacOSFileSystemExtension));
      } else if (OperatingSystem.IsLinux()) {
        Assert.IsInstanceOfType(extension, typeof(mtsuite.CoreFileSystem.LinuxFileSystemExtension));
      } else if (OperatingSystem.IsWindows()) {
        Assert.IsInstanceOfType(extension, typeof(mtsuite.CoreFileSystem.WindowsFileSystemExtension));
      }
    }

    [TestMethod]
    public void MacOSFileSystemExtension_CloneFile_DoesNotSwallowException_LastWriteTime() {
      if (!OperatingSystem.IsMacOS()) {
        Assert.Inconclusive("Test only runs on macOS.");
      }

      var poolFactory = new mtsuite.CoreFileSystem.ObjectPool.MtPoolFactory();
      var extension = new mtsuite.CoreFileSystem.MacOSFileSystemExtension(poolFactory);

      var sourceFile = _fileSystemSetup.Root.CreateFile("source_file.txt", 100);
      var destinationPath = _fileSystemSetup.Root.Path.Combine("dest_file.txt");

      var entry = _fileSystemSetup.FileSystem.GetEntry(sourceFile.Path);

      // Construct an entry with invalid LastWriteTimeUtc ticks (e.g. long.MaxValue)
      var invalidData = new mtsuite.CoreFileSystem.FileSystemEntryData(
        System.IO.FileAttributes.Normal,
        entry.FileSize,
        long.MaxValue
      );
      var sourceEntry = new mtsuite.CoreFileSystem.FileSystemEntry(sourceFile.Path, invalidData);

      Assert.ThrowsException<ArgumentOutOfRangeException>(() => {
        extension.CloneFile(sourceEntry, destinationPath);
      });
    }

    [TestMethod]
    public void MacOSFileSystemExtension_AreFilesCloned_ReturnsTrueForClonedFiles() {
      if (!OperatingSystem.IsMacOS()) {
        Assert.Inconclusive("Test only runs on macOS.");
      }

      var poolFactory = new mtsuite.CoreFileSystem.ObjectPool.MtPoolFactory();
      var extension = new mtsuite.CoreFileSystem.MacOSFileSystemExtension(poolFactory);

      var sourceFile = _fileSystemSetup.Root.CreateFile("source_file.txt", 100);
      var destinationFile = _fileSystemSetup.Root.CreateFile("dest_file.txt", 100);

      var entry1 = _fileSystemSetup.FileSystem.GetEntry(sourceFile.Path);
      var entry2 = _fileSystemSetup.FileSystem.GetEntry(destinationFile.Path);

      // Verify that before cloning, they are not considered cloned
      Assert.IsFalse(extension.AreFilesCloned(entry1, entry2));

      // Clone it
      _fileSystemSetup.FileSystem.DeleteEntry(entry2);
      extension.CloneFile(entry1, destinationFile.Path);

      // Verify that after cloning, AreFilesCloned returns true!
      var clonedEntry2 = _fileSystemSetup.FileSystem.GetEntry(destinationFile.Path);
      Assert.IsTrue(extension.AreFilesCloned(entry1, clonedEntry2));
    }

    [TestMethod]
    public void MacOSFileSystemExtension_AreFilesCloned_ReturnsTrueForZeroByteFiles() {
      if (!OperatingSystem.IsMacOS()) {
        Assert.Inconclusive("Test only runs on macOS.");
      }

      var poolFactory = new mtsuite.CoreFileSystem.ObjectPool.MtPoolFactory();
      var extension = new mtsuite.CoreFileSystem.MacOSFileSystemExtension(poolFactory);

      var sourceFile = _fileSystemSetup.Root.CreateFile("source_file_zero.txt", 0);
      var destinationFile = _fileSystemSetup.Root.CreateFile("dest_file_zero.txt", 0);

      var entry1 = _fileSystemSetup.FileSystem.GetEntry(sourceFile.Path);
      var entry2 = _fileSystemSetup.FileSystem.GetEntry(destinationFile.Path);
      Assert.IsTrue(extension.AreFilesCloned(entry1, entry2));
    }

    [TestMethod]
    public void Extension_DeleteDirectoryEntries_DeletesFilesAndDirectories() {
      var root = _fileSystemSetup.Root;
      var file1 = root.CreateFile("file1.txt", 10);
      var file2 = root.CreateFile("file2.txt", 20);
      var subDir = root.CreateDirectory("subDir");
      var subFile = subDir.CreateFile("subFile.txt", 5);

      var fs = _fileSystemSetup.FileSystem;
      using var subEntries = fs.GetDirectoryFiles(subDir.Path);
      object state = null;
      fs.Extension.DeleteDirectoryEntries<object>(
        fs.GetEntry(subDir.Path),
        subEntries.Item,
        ref state,
        static (System.Collections.Generic.IReadOnlyList<mtsuite.CoreFileSystem.FileSystemEntry> entries, int index, ref object s) => true,
        static (System.Collections.Generic.IReadOnlyList<mtsuite.CoreFileSystem.FileSystemEntry> entries, int index, Exception ex, ref object s) => {});
      Assert.IsFalse(subFile.Exists());

      using var rootEntries = fs.GetDirectoryFiles(root.Path);
      fs.Extension.DeleteDirectoryEntries<object>(
        fs.GetEntry(root.Path),
        rootEntries.Item,
        ref state,
        static (System.Collections.Generic.IReadOnlyList<mtsuite.CoreFileSystem.FileSystemEntry> entries, int index, ref object s) => true,
        static (System.Collections.Generic.IReadOnlyList<mtsuite.CoreFileSystem.FileSystemEntry> entries, int index, Exception ex, ref object s) => {});

      Assert.IsFalse(file1.Exists());
      Assert.IsFalse(file2.Exists());
      Assert.IsFalse(subDir.Exists());
    }

    [TestMethod]
    public void Extension_DeleteDirectoryEntries_DeletesSymbolicLinksWithoutDeletingTarget() {
      if (!_fileSystemSetup.SupportsSymbolicLinkCreation()) {
        Assert.Inconclusive("Symbolic links are not supported.");
      }

      var root = _fileSystemSetup.Root;
      var targetDir = root.CreateDirectory("targetDir");
      var targetFile = targetDir.CreateFile("target.txt", 10);
      var linkDir = root.CreateDirectoryLink("linkDir", "targetDir");
      var linkFile = root.CreateFileLink("linkFile.txt", "targetDir/target.txt");

      var fs = _fileSystemSetup.FileSystem;
      using var rootEntries = fs.GetDirectoryFiles(root.Path);
      // Filter to just the links
      var linksOnly = rootEntries.Item.FindAll(e => e.IsReparsePoint);
      object state = null;
      fs.Extension.DeleteDirectoryEntries<object>(
        fs.GetEntry(root.Path),
        linksOnly,
        ref state,
        static (System.Collections.Generic.IReadOnlyList<mtsuite.CoreFileSystem.FileSystemEntry> entries, int index, ref object s) => true,
        static (System.Collections.Generic.IReadOnlyList<mtsuite.CoreFileSystem.FileSystemEntry> entries, int index, Exception ex, ref object s) => {});

      Assert.IsFalse(linkDir.Exists());
      Assert.IsFalse(linkFile.Exists());
      Assert.IsTrue(targetDir.Exists());
      Assert.IsTrue(targetFile.Exists());
    }

    [TestMethod]
    public void WindowsFileSystemExtension_AreFilesCloned_ReturnsTrueForZeroByteFiles() {
      if (!OperatingSystem.IsWindows()) {
        Assert.Inconclusive("Test only runs on Windows.");
      }

      var poolFactory = new mtsuite.CoreFileSystem.ObjectPool.MtPoolFactory();
      var extension = new mtsuite.CoreFileSystem.WindowsFileSystemExtension(poolFactory);

      var sourceFile = _fileSystemSetup.Root.CreateFile("source_file_zero_win.txt", 0);
      var destinationFile = _fileSystemSetup.Root.CreateFile("dest_file_zero_win.txt", 0);

      var entry1 = _fileSystemSetup.FileSystem.GetEntry(sourceFile.Path);
      var entry2 = _fileSystemSetup.FileSystem.GetEntry(destinationFile.Path);
      Assert.IsTrue(extension.AreFilesCloned(entry1, entry2));
    }

    [TestMethod]
    public void WindowsFileSystemExtension_CloneFile_DoesNotSwallowException_LastWriteTime() {
      if (!OperatingSystem.IsWindows()) {
        Assert.Inconclusive("Test only runs on Windows.");
      }

      var poolFactory = new mtsuite.CoreFileSystem.ObjectPool.MtPoolFactory();
      var extension = new mtsuite.CoreFileSystem.WindowsFileSystemExtension(poolFactory);

      var sourceFile = _fileSystemSetup.Root.CreateFile("source_file_win.txt", 100);
      var destinationPath = _fileSystemSetup.Root.Path.Combine("dest_file_win.txt");

      var entry = _fileSystemSetup.FileSystem.GetEntry(sourceFile.Path);

      // Construct an entry with invalid LastWriteTimeUtc ticks (e.g. long.MaxValue)
      var invalidData = new mtsuite.CoreFileSystem.FileSystemEntryData(
        System.IO.FileAttributes.Normal,
        entry.FileSize,
        long.MaxValue
      );
      var sourceEntry = new mtsuite.CoreFileSystem.FileSystemEntry(sourceFile.Path, invalidData);

      Assert.ThrowsException<ArgumentOutOfRangeException>(() => {
        extension.CloneFile(sourceEntry, destinationPath);
      });
    }

    [TestMethod]
    public void WindowsFileSystemExtension_ReFSBlockCloning_WorksWhenSupported() {
      if (!OperatingSystem.IsWindows()) {
        Assert.Inconclusive("Test only runs on Windows.");
      }

      // Check if any drive is formatted with ReFS
      string refsDrive = null;
      foreach (var drive in System.IO.DriveInfo.GetDrives()) {
        try {
          if (drive.IsReady && string.Equals(drive.DriveFormat, "ReFS", StringComparison.OrdinalIgnoreCase)) {
            refsDrive = drive.RootDirectory.FullName;
            break;
          }
        } catch { }
      }

      if (refsDrive == null) {
        Assert.Inconclusive("No ReFS drive available for block cloning test.");
        return;
      }

      var poolFactory = new mtsuite.CoreFileSystem.ObjectPool.MtPoolFactory();
      var extension = new mtsuite.CoreFileSystem.WindowsFileSystemExtension(poolFactory);
      var fs = mtsuite.CoreFileSystem.FileSystem.CreateDefault(poolFactory);

      string testDir = System.IO.Path.Combine(refsDrive, "mtsuite_refs_test_" + Guid.NewGuid().ToString("N"));
      System.IO.Directory.CreateDirectory(testDir);
      try {
        var testDirPath = new mtsuite.CoreFileSystem.FullPath(testDir);
        Assert.IsTrue(extension.IsCloningSupported(testDirPath, testDirPath));

        // Create test file (128 KB with non-trivial byte content)
        string srcFile = System.IO.Path.Combine(testDir, "test_source.dat");
        byte[] expectedBytes = new byte[128 * 1024 + 123]; // Non-cluster-aligned size to test tail remainder
        for (int i = 0; i < expectedBytes.Length; i++) {
          expectedBytes[i] = (byte)(i % 251);
        }
        System.IO.File.WriteAllBytes(srcFile, expectedBytes);

        var srcEntry = fs.GetEntry(new mtsuite.CoreFileSystem.FullPath(srcFile));
        var dstPath = new mtsuite.CoreFileSystem.FullPath(System.IO.Path.Combine(testDir, "test_clone.dat"));

        // Clone file
        extension.CloneFile(srcEntry, dstPath);

        // Verify clone exists and matches content
        Assert.IsTrue(System.IO.File.Exists(dstPath.FullName));
        byte[] clonedBytes = System.IO.File.ReadAllBytes(dstPath.FullName);
        CollectionAssert.AreEqual(expectedBytes, clonedBytes);

        // Verify timestamps match
        var dstEntry = fs.GetEntry(dstPath);
        Assert.AreEqual(srcEntry.LastWriteTimeUtc, dstEntry.LastWriteTimeUtc);

        // Verify AreFilesCloned returns true for cloned files
        bool areCloned = extension.AreFilesCloned(srcEntry, dstEntry);
        Assert.IsTrue(areCloned, "Expected AreFilesCloned to return true for freshly cloned ReFS file");

        // Verify that modifying destination makes them no longer cloned
        System.IO.File.WriteAllBytes(dstPath.FullName, new byte[expectedBytes.Length]);
        var modifiedDstEntry = fs.GetEntry(dstPath);
        Assert.IsFalse(extension.AreFilesCloned(srcEntry, modifiedDstEntry));
      }
      finally {
        try { System.IO.Directory.Delete(testDir, recursive: true); } catch { }
      }
    }

    [TestMethod]
    public void NullFileSystemExtensionBehavesSafely() {
      var poolFactory = new mtsuite.CoreFileSystem.ObjectPool.MtPoolFactory();
      var nullExt = new mtsuite.CoreFileSystem.NullFileSystemExtension(poolFactory);
      var pathA = OperatingSystem.IsWindows() ? new mtsuite.CoreFileSystem.FullPath(@"C:\tmp\a") : new mtsuite.CoreFileSystem.FullPath("/tmp/a");
      var pathB = OperatingSystem.IsWindows() ? new mtsuite.CoreFileSystem.FullPath(@"C:\tmp\b") : new mtsuite.CoreFileSystem.FullPath("/tmp/b");
      Assert.IsFalse(nullExt.IsCloningSupported(pathA, pathB));
      Assert.IsFalse(nullExt.AreFilesCloned(default(mtsuite.CoreFileSystem.FileSystemEntry), default(mtsuite.CoreFileSystem.FileSystemEntry)));
      Assert.ThrowsException<PlatformNotSupportedException>(() =>
        nullExt.CloneFile(default, pathB));
    }
  }
}
