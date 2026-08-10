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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using mtcompact;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.shared;
using mtsuite.shared.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class MtCompactTest {
    private FileSystemSetup _sourcefs;
    private FileSystemSetup _destfs;
    private MtPoolFactory _poolFactory;
    private IFileComparer _fileComparer;

    /// <summary>
    /// Test file system that records clone operations without requiring OS-level CoW support (e.g. on Linux/Windows),
    /// enabling complete verification of which files are selected for cloning vs skipped or untouched.
    /// </summary>
    private class TestCompactFileSystem : IFileSystem, IFileSystemExtension {
      private readonly IFileSystem _inner;

      public TestCompactFileSystem(IFileSystem inner) {
        _inner = inner;
      }

      public IFileSystemExtension Extension => this;
      public bool SupportsCloningValue { get; set; } = true;
      public List<(string Source, string Destination)> ClonedPairs { get; } = new();

      public bool IsCloningSupported(FullPath sourcePath, FullPath destinationPath) => SupportsCloningValue;
      public FileSystemEntry GetEntry(FullPath path) => _inner.GetEntry(path);
      public ReparsePointInfo GetReparsePointInfo(FullPath path) => _inner.GetReparsePointInfo(path);
      public FromPool<List<FileSystemEntry>> GetDirectoryFiles(FullPath path) => _inner.GetDirectoryFiles(path);
      public void CreateDirectory(FullPath path) => _inner.CreateDirectory(path);
      public void DeleteEntry(FileSystemEntry entry) => _inner.DeleteEntry(entry);
      public void CopyFile<T>(FileSystemEntry sourceEntry, FullPath destinationPath, CopyFileOptions options, T param, CopyFileCallback<T> callback) =>
        _inner.CopyFile(sourceEntry, destinationPath, options, param, callback);
      public void CopyFile<T>(FileSystemEntry sourceEntry, FileSystemEntry destinationEntry, CopyFileOptions options, T param, CopyFileCallback<T> callback) =>
        _inner.CopyFile(sourceEntry, destinationEntry, options, param, callback);
      public FileStream OpenFile(FullPath path, FileAccess access) => _inner.OpenFile(path, access);

      public void CloneFile(FileSystemEntry sourceEntry, FullPath destinationPath) {
        lock (ClonedPairs) {
          ClonedPairs.Add((sourceEntry.Path.FullName, destinationPath.FullName));
        }
        if (File.Exists(destinationPath.FullName)) {
          File.Delete(destinationPath.FullName);
        }
        _inner.CopyFile(sourceEntry, destinationPath, CopyFileOptions.Default, (object)null, static (ref FileSystemEntry s, long v, long c, long t, ref object p) => { });
      }

      public HashSet<(string Source, string Destination)> AlreadyClonedPairs { get; } = new();

      public bool AreFilesCloned(FileSystemEntry file1, FileSystemEntry file2) {
        lock (AlreadyClonedPairs) {
          return AlreadyClonedPairs.Contains((file1.Path.FullName, file2.Path.FullName));
        }
      }

      public void DeleteDirectoryEntries(FileSystemEntry directory, IReadOnlyList<FileSystemEntry> entries) =>
        _inner.Extension.DeleteDirectoryEntries(directory, entries);
    }

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
    public void MtCompactShouldThrowWithNonExistingFolder() {
      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem);
      var mtcompact = new MtCompact(testFs, _poolFactory);
      mtcompact.DoCompact(_sourcefs.Root.Path.Combine("fake"), _destfs.Root.Path, _fileComparer, isDryRun: false);
    }

    [TestMethod]
    public void MtCompactShouldWorkWithEmptyFolders() {
      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem);
      var mtcompact = new MtCompact(testFs, _poolFactory);

      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer, isDryRun: false);
      Assert.AreEqual(0, stats.FileClonedCount);
      Assert.AreEqual(0, stats.FileCloneSkippedCount);
      Assert.AreEqual(0, stats.Errors.Count);
      Assert.AreEqual(0, testFs.ClonedPairs.Count);
    }

    [TestMethod]
    public void MtCompactShouldNotModifyAnythingWhenNoFilesAreInCommon() {
      // Source files
      _sourcefs.Root.CreateFile("source_a.txt", 100);
      _sourcefs.Root.CreateFile("source_b.txt", 200);
      var sourceSub = _sourcefs.Root.CreateDirectory("source_sub");
      sourceSub.CreateFile("source_c.txt", 300);

      // Destination files (disjoint from source)
      _destfs.Root.CreateFile("dest_x.txt", 400);
      _destfs.Root.CreateFile("dest_y.txt", 500);
      var destSub = _destfs.Root.CreateDirectory("dest_sub");
      destSub.CreateFile("dest_z.txt", 600);

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem);
      var mtcompact = new MtCompact(testFs, _poolFactory);

      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer, isDryRun: false);

      // Verify zero files were compacted or cloned
      Assert.AreEqual(0, stats.FileClonedCount);
      Assert.AreEqual(0, stats.FileClonedTotalSize);
      Assert.AreEqual(0, stats.FileCloneSkippedCount); // source_a.txt and source_b.txt are not in destination!
      Assert.AreEqual(0, stats.Errors.Count);
      Assert.AreEqual(0, testFs.ClonedPairs.Count);

      // Verify all destination files remain intact and unchanged
      Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("dest_x.txt").FullName));
      Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("dest_y.txt").FullName));
      Assert.IsTrue(File.Exists(_destfs.Root.Path.Combine("dest_sub").Combine("dest_z.txt").FullName));
      Assert.AreEqual(400, new FileInfo(_destfs.Root.Path.Combine("dest_x.txt").FullName).Length);
      Assert.AreEqual(500, new FileInfo(_destfs.Root.Path.Combine("dest_y.txt").FullName).Length);
      Assert.AreEqual(600, new FileInfo(_destfs.Root.Path.Combine("dest_sub").Combine("dest_z.txt").FullName).Length);

      // Verify source files remain intact and unchanged
      Assert.IsTrue(File.Exists(_sourcefs.Root.Path.Combine("source_a.txt").FullName));
      Assert.IsTrue(File.Exists(_sourcefs.Root.Path.Combine("source_b.txt").FullName));
      Assert.IsTrue(File.Exists(_sourcefs.Root.Path.Combine("source_sub").Combine("source_c.txt").FullName));
    }

    [TestMethod]
    public void MtCompactShouldCloneIdenticalFilesAndRecordClonedPairs() {
      // Prepare identical files
      _sourcefs.Root.CreateFile("file1.txt", 100);
      _destfs.Root.CreateFile("file1.txt", 100);
      _sourcefs.Root.CreateFile("file2.txt", 200);
      _destfs.Root.CreateFile("file2.txt", 200);

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem);
      var mtcompact = new MtCompact(testFs, _poolFactory);

      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer, isDryRun: false);
      Assert.AreEqual(2, stats.FileClonedCount);
      Assert.AreEqual(300, stats.FileClonedTotalSize);
      Assert.AreEqual(0, stats.FileCloneSkippedCount);
      Assert.AreEqual(0, stats.Errors.Count);

      // Verify the exact source->destination pairs that were cloned
      Assert.AreEqual(2, testFs.ClonedPairs.Count);
      Assert.IsTrue(testFs.ClonedPairs.Exists(p =>
        p.Source == _sourcefs.Root.Path.Combine("file1.txt").FullName &&
        p.Destination == _destfs.Root.Path.Combine("file1.txt").FullName));
      Assert.IsTrue(testFs.ClonedPairs.Exists(p =>
        p.Source == _sourcefs.Root.Path.Combine("file2.txt").FullName &&
        p.Destination == _destfs.Root.Path.Combine("file2.txt").FullName));
    }

    [TestMethod]
    public void MtCompactShouldOnlyCloneIdenticalFilesAndSkipNonIdentical() {
      // file1 has different size
      _sourcefs.Root.CreateFile("file1.txt", 100);
      _destfs.Root.CreateFile("file1.txt", 200);

      // file2 is identical
      _sourcefs.Root.CreateFile("file2.txt", 300);
      _destfs.Root.CreateFile("file2.txt", 300);

      // file3 exists only in source
      _sourcefs.Root.CreateFile("file3.txt", 400);

      // file4 exists only in dest
      _destfs.Root.CreateFile("file4.txt", 500);

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem);
      var mtcompact = new MtCompact(testFs, _poolFactory);

      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer, isDryRun: false);
      Assert.AreEqual(1, stats.FileClonedCount);
      Assert.AreEqual(300, stats.FileClonedTotalSize);
      Assert.AreEqual(1, stats.FileCloneSkippedCount); // file1 (different)
      Assert.AreEqual(0, stats.Errors.Count);

      // Verify only file2 was cloned
      Assert.AreEqual(1, testFs.ClonedPairs.Count);
      Assert.AreEqual(_sourcefs.Root.Path.Combine("file2.txt").FullName, testFs.ClonedPairs[0].Source);
      Assert.AreEqual(_destfs.Root.Path.Combine("file2.txt").FullName, testFs.ClonedPairs[0].Destination);

      // Verify destination non-identical and extra files remain untouched
      Assert.AreEqual(200, new FileInfo(_destfs.Root.Path.Combine("file1.txt").FullName).Length);
      Assert.AreEqual(500, new FileInfo(_destfs.Root.Path.Combine("file4.txt").FullName).Length);
    }

    [TestMethod]
    public void MtCompactShouldCompareFileContentsWithContentComparer() {
      // 1. diff_content.txt has same size (5 bytes) but different bytes and different timestamps
      var srcPath1 = _sourcefs.Root.Path.Combine("diff_content.txt").FullName;
      var dstPath1 = _destfs.Root.Path.Combine("diff_content.txt").FullName;
      File.WriteAllBytes(srcPath1, new byte[] { 1, 2, 3, 4, 5 });
      File.WriteAllBytes(dstPath1, new byte[] { 1, 2, 3, 4, 9 });
      File.SetLastWriteTimeUtc(srcPath1, DateTime.UtcNow.AddMinutes(-10));
      File.SetLastWriteTimeUtc(dstPath1, DateTime.UtcNow.AddMinutes(-5));

      // 2. same_content_diff_time.txt has same bytes but different timestamps
      var srcPath2 = _sourcefs.Root.Path.Combine("same_content_diff_time.txt").FullName;
      var dstPath2 = _destfs.Root.Path.Combine("same_content_diff_time.txt").FullName;
      File.WriteAllBytes(srcPath2, new byte[] { 10, 20, 30, 40 });
      File.WriteAllBytes(dstPath2, new byte[] { 10, 20, 30, 40 });
      File.SetLastWriteTimeUtc(srcPath2, DateTime.UtcNow.AddMinutes(-20));
      File.SetLastWriteTimeUtc(dstPath2, DateTime.UtcNow.AddMinutes(-1));

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem);
      var mtcompact = new MtCompact(testFs, _poolFactory);
      var contentComparer = new FileContentsFileComparer(testFs, _poolFactory);

      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, contentComparer, isDryRun: false);
      Assert.AreEqual(1, stats.FileClonedCount); // only same_content_diff_time.txt
      Assert.AreEqual(1, stats.FileCloneSkippedCount); // diff_content.txt
      Assert.AreEqual(1, testFs.ClonedPairs.Count);
      Assert.AreEqual(srcPath2, testFs.ClonedPairs[0].Source);
      Assert.AreEqual(dstPath2, testFs.ClonedPairs[0].Destination);

      // Verify destination diff_content.txt was NOT overwritten
      var dstBytes = File.ReadAllBytes(dstPath1);
      Assert.AreEqual(9, dstBytes[4]);
    }

    [TestMethod]
    public void MtCompactShouldNotCloneFilesWithSameTimestampAndSizeButDifferentContent() {
      var srcPath = _sourcefs.Root.Path.Combine("same_time_diff_content.txt").FullName;
      var dstPath = _destfs.Root.Path.Combine("same_time_diff_content.txt").FullName;

      // Both files have 10 bytes and identical timestamp, but different byte contents
      var sharedTimestamp = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
      File.WriteAllBytes(srcPath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
      File.WriteAllBytes(dstPath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 99 });
      File.SetLastWriteTimeUtc(srcPath, sharedTimestamp);
      File.SetLastWriteTimeUtc(dstPath, sharedTimestamp);

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem);
      var mtcompact = new MtCompact(testFs, _poolFactory);
      var contentComparer = new FileContentsFileComparer(testFs, _poolFactory);

      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, contentComparer, isDryRun: false);

      // Must NOT be cloned because contents differ
      Assert.AreEqual(0, stats.FileClonedCount);
      Assert.AreEqual(1, stats.FileCloneSkippedCount);
      Assert.AreEqual(0, testFs.ClonedPairs.Count);
      Assert.AreEqual(99, File.ReadAllBytes(dstPath)[9]);
    }

    [TestMethod]
    public void MtCompactShouldShareFilesInNestedDirectories() {
      var sourceSub = _sourcefs.Root.CreateDirectory("sub");
      var destSub = _destfs.Root.CreateDirectory("sub");

      sourceSub.CreateFile("nested.txt", 500);
      destSub.CreateFile("nested.txt", 500);

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem);
      var mtcompact = new MtCompact(testFs, _poolFactory);

      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer, isDryRun: false);
      Assert.AreEqual(1, stats.FileClonedCount);
      Assert.AreEqual(500, stats.FileClonedTotalSize);
      Assert.AreEqual(0, stats.Errors.Count);

      Assert.AreEqual(1, testFs.ClonedPairs.Count);
      Assert.AreEqual(sourceSub.Path.Combine("nested.txt").FullName, testFs.ClonedPairs[0].Source);
      Assert.AreEqual(destSub.Path.Combine("nested.txt").FullName, testFs.ClonedPairs[0].Destination);
    }

    [TestMethod]
    public void MtCompactShouldFallbackToSimulationModeWhenCloningUnsupported() {
      // Setup identical files
      _sourcefs.Root.CreateFile("file1.txt", 500);
      _destfs.Root.CreateFile("file1.txt", 500);
      _sourcefs.Root.CreateFile("file2.txt", 300);
      _destfs.Root.CreateFile("file2.txt", 300);
      _sourcefs.Root.CreateFile("different.txt", 100);
      _destfs.Root.CreateFile("different.txt", 200);

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem) {
        SupportsCloningValue = false // Simulate non-supporting OS/filesystem
      };

      var mtcompact = new MtCompact(testFs, _poolFactory);
      // Run with standard CLI arguments
      mtcompact.Run(new[] { _sourcefs.Root.Path.FullName, _destfs.Root.Path.FullName });

      // In simulation mode: zero files modified/cloned on disk, but potential savings calculated accurately
      Assert.AreEqual(0, testFs.ClonedPairs.Count);
    }

    [TestMethod]
    public void MtCompactShouldSupportDryRunOption() {
      _sourcefs.Root.CreateFile("file1.txt", 500);
      _destfs.Root.CreateFile("file1.txt", 500);

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem) {
        SupportsCloningValue = true
      };

      var mtcompact = new MtCompact(testFs, _poolFactory);
      mtcompact.Run(new[] { _sourcefs.Root.Path.FullName, _destfs.Root.Path.FullName, "--dry-run" });

      // In dry-run mode, no clone operations performed
      Assert.AreEqual(0, testFs.ClonedPairs.Count);
    }

    [TestMethod]
    public void MtCompactShouldDefaultToContentComparisonViaCli() {
      // Create files with identical content but different modification timestamps
      var srcPath = _sourcefs.Root.Path.Combine("diff_time_same_content.txt").FullName;
      var dstPath = _destfs.Root.Path.Combine("diff_time_same_content.txt").FullName;
      File.WriteAllBytes(srcPath, new byte[] { 1, 2, 3, 4 });
      File.WriteAllBytes(dstPath, new byte[] { 1, 2, 3, 4 });
      File.SetLastWriteTimeUtc(srcPath, DateTime.UtcNow.AddHours(-5));
      File.SetLastWriteTimeUtc(dstPath, DateTime.UtcNow.AddHours(-1));

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem) {
        SupportsCloningValue = true
      };

      var mtcompact = new MtCompact(testFs, _poolFactory);
      // Run with default CLI args (no -fc or -ft)
      mtcompact.Run(new[] { _sourcefs.Root.Path.FullName, _destfs.Root.Path.FullName });

      // Should have cloned because content comparison is the default
      Assert.AreEqual(1, testFs.ClonedPairs.Count);
      Assert.AreEqual(srcPath, testFs.ClonedPairs[0].Source);
      Assert.AreEqual(dstPath, testFs.ClonedPairs[0].Destination);
    }

    [TestMethod]
    public void MtCompactShouldProcessManyFilesInParallel() {
      // Create 20 files with varying sizes and content matches
      for (int i = 0; i < 20; i++) {
        var srcPath = _sourcefs.Root.Path.Combine($"file_{i}.dat").FullName;
        var dstPath = _destfs.Root.Path.Combine($"file_{i}.dat").FullName;

        if (i % 2 == 0) {
          // Identical content, different timestamp
          byte[] content = new byte[1024];
          Array.Fill(content, (byte)i);
          File.WriteAllBytes(srcPath, content);
          File.WriteAllBytes(dstPath, content);
        } else {
          // Different content
          byte[] srcContent = new byte[1024];
          byte[] dstContent = new byte[1024];
          Array.Fill(srcContent, (byte)i);
          Array.Fill(dstContent, (byte)(i + 100));
          File.WriteAllBytes(srcPath, srcContent);
          File.WriteAllBytes(dstPath, dstContent);
        }
        File.SetLastWriteTimeUtc(srcPath, DateTime.UtcNow.AddHours(-10));
        File.SetLastWriteTimeUtc(dstPath, DateTime.UtcNow.AddHours(-1));
      }

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem) {
        SupportsCloningValue = true
      };
      var mtcompact = new MtCompact(testFs, _poolFactory);
      var contentComparer = new FileContentsFileComparer(testFs, _poolFactory);

      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, contentComparer, isDryRun: false);
      Assert.AreEqual(10, stats.FileClonedCount); // 10 even files
      Assert.AreEqual(10, stats.FileCloneSkippedCount); // 10 odd files
      Assert.AreEqual(10, testFs.ClonedPairs.Count);
    }

    [TestMethod]
    public void FileComparerIsFastPropertyWorks() {
      var fastComparer = new LastWriteTimeFileComparer(_sourcefs.FileSystem);
      var slowComparer = new FileContentsFileComparer(_sourcefs.FileSystem, _poolFactory);

      Assert.IsTrue(fastComparer.IsFast);
      Assert.IsFalse(slowComparer.IsFast);
    }

    [TestMethod]
    public void ParallelFileSystemShouldSkipComparingEventsForFastComparer() {
      _sourcefs.Root.CreateFile("file1.txt", 100);
      _destfs.Root.CreateFile("file1.txt", 100);

      var parallelFs = new ParallelFileSystem(_sourcefs.FileSystem, _poolFactory);
      int comparingEventCount = 0;
      int comparedEventCount = 0;
      parallelFs.FileComparing += _ => Interlocked.Increment(ref comparingEventCount);
      parallelFs.FileCompared += (_, _, _) => Interlocked.Increment(ref comparedEventCount);

      // Fast comparer (LastWriteTime) will trigger 1 FileComparing / FileCompared event during AreFilesCloned
      var fastComparer = new LastWriteTimeFileComparer(_sourcefs.FileSystem);
      var sourceDir = _sourcefs.FileSystem.GetEntry(_sourcefs.Root.Path);
      var desDir = _destfs.FileSystem.GetEntry(_destfs.Root.Path);
      var task = parallelFs.CompactDirectoryAsync(sourceDir, desDir, fastComparer, true);
      parallelFs.WaitForTask(task);
      Assert.AreEqual(1, comparingEventCount);
      Assert.AreEqual(1, comparedEventCount);

      // Slow comparer (FileContents) should trigger FileComparing / FileCompared events
      var slowComparer = new FileContentsFileComparer(_sourcefs.FileSystem, _poolFactory);
      task = parallelFs.CompactDirectoryAsync(sourceDir, desDir, slowComparer, true);
      parallelFs.WaitForTask(task);

      Assert.AreEqual(2, comparingEventCount);
      Assert.AreEqual(2, comparedEventCount);
    }

    [TestMethod]
    public void MtCompactShouldSkipAndReportAlreadyClonedFiles() {
      // Prepare identical files
      var srcFile1 = _sourcefs.Root.CreateFile("file1.txt", 100);
      var srcFile2 = _sourcefs.Root.CreateFile("file2.txt", 200);
      var dstFile1 = _destfs.Root.CreateFile("file1.txt", 100);
      var dstFile2 = _destfs.Root.CreateFile("file2.txt", 200);

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem) {
        SupportsCloningValue = true
      };
      // Mark file1 and file2 as already cloned
      testFs.AlreadyClonedPairs.Add((srcFile1.Path.FullName, dstFile1.Path.FullName));
      testFs.AlreadyClonedPairs.Add((srcFile2.Path.FullName, dstFile2.Path.FullName));

      var mtcompact = new MtCompact(testFs, _poolFactory);
      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer, isDryRun: false);

      // Verify files were skipped from cloning and recorded as already cloned
      Assert.AreEqual(0, stats.FileClonedCount);
      Assert.AreEqual(0, stats.FileClonedTotalSize);
      Assert.AreEqual(2, stats.FileAlreadyClonedCount);
      Assert.AreEqual(300, stats.FileAlreadyClonedTotalSize);
      Assert.AreEqual(0, stats.FileCloneSkippedCount);
      Assert.AreEqual(0, testFs.ClonedPairs.Count);
    }

    [TestMethod]
    public void MtCompactShouldHandleMixOfAlreadyClonedAndNewClones() {
      // Prepare 3 files:
      // file1: already cloned
      // file2: identical, not yet cloned -> should be cloned
      // file3: different size/content -> skipped
      var srcFile1 = _sourcefs.Root.CreateFile("file1.txt", 100);
      var srcFile2 = _sourcefs.Root.CreateFile("file2.txt", 200);
      var srcFile3 = _sourcefs.Root.CreateFile("file3.txt", 300);

      var dstFile1 = _destfs.Root.CreateFile("file1.txt", 100);
      var dstFile2 = _destfs.Root.CreateFile("file2.txt", 200);
      var dstFile3 = _destfs.Root.CreateFile("file3.txt", 350);

      var testFs = new TestCompactFileSystem(_sourcefs.FileSystem) {
        SupportsCloningValue = true
      };
      // Mark file1 as already cloned
      testFs.AlreadyClonedPairs.Add((srcFile1.Path.FullName, dstFile1.Path.FullName));

      var mtcompact = new MtCompact(testFs, _poolFactory);
      var stats = mtcompact.DoCompact(_sourcefs.Root.Path, _destfs.Root.Path, _fileComparer, isDryRun: false);

      Assert.AreEqual(1, stats.FileClonedCount);
      Assert.AreEqual(200, stats.FileClonedTotalSize);
      Assert.AreEqual(1, stats.FileAlreadyClonedCount);
      Assert.AreEqual(100, stats.FileAlreadyClonedTotalSize);
      Assert.AreEqual(1, stats.FileCloneSkippedCount); // file3 has different size
      Assert.AreEqual(1, testFs.ClonedPairs.Count);
      Assert.AreEqual((srcFile2.Path.FullName, dstFile2.Path.FullName), testFs.ClonedPairs[0]);
    }
  }
}
