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
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtsuite.CoreFileSystem;
using mtsuite.shared;
using mtsuite.shared.Utils;

namespace tests {
  [TestClass]
  public class ThreadProgressTrackerTest {
    [TestMethod]
    public void FormatSizeWorksCorrectly() {
      Assert.AreEqual("0 B", FormatHelpers.FormatSize(0));
      Assert.AreEqual("0 B", FormatHelpers.FormatSize(-10));
      Assert.AreEqual("512 B", FormatHelpers.FormatSize(512));
      Assert.AreEqual("1.0 KB", FormatHelpers.FormatSize(1024));
      Assert.AreEqual("1.5 KB", FormatHelpers.FormatSize(1536));
      Assert.AreEqual("1.0 MB", FormatHelpers.FormatSize(1024 * 1024));
      Assert.AreEqual("50.0 MB", FormatHelpers.FormatSize(50 * 1024 * 1024));
      Assert.AreEqual("1.00 GB", FormatHelpers.FormatSize(1024L * 1024 * 1024));
      Assert.AreEqual("2.50 GB", FormatHelpers.FormatSize((long)(2.5 * 1024 * 1024 * 1024)));
    }

    [TestMethod]
    public void TruncateMiddleWorksCorrectly() {
      Assert.AreEqual("short", FormatHelpers.TruncateMiddle("short", 10));
      Assert.AreEqual("Hel...ld", FormatHelpers.TruncateMiddle("Hello World", 8));
      Assert.AreEqual("abc", FormatHelpers.TruncateMiddle("abcdefghij", 3));
      Assert.AreEqual("a...j", FormatHelpers.TruncateMiddle("abcdefghij", 5));
    }

    [TestMethod]
    public void TruncatePathWorksCorrectly() {
      Assert.AreEqual("a/b/c.txt", FormatHelpers.TruncatePath("a/b/c.txt", 20));
      Assert.AreEqual("...path/to/file.txt", FormatHelpers.TruncatePath("/very/long/path/to/file.txt", 19));
    }

    [TestMethod]
    public void StripAnsiAndCountVisualLinesWork() {
      var ansiLine = "\u001B[K  Thread  1: Copying file.dat\u001B[0m";
      Assert.AreEqual("  Thread  1: Copying file.dat", ProgressPrinter.StripAnsi(ansiLine));

      // 40-character line on 80-width terminal = 1 visual line
      var shortText = "1234567890123456789012345678901234567890";
      Assert.AreEqual(1, ProgressPrinter.CountVisualLines(shortText, 80));

      // 100-character line on 80-width terminal = 2 visual lines
      var longText = new string('x', 100);
      Assert.AreEqual(2, ProgressPrinter.CountVisualLines(longText, 80));

      // Multi-line text with wrapping: line 1 (100 chars -> 2 rows), line 2 (50 chars -> 1 row) = 3 rows
      var multiLine = new string('x', 100) + "\n" + new string('y', 50);
      Assert.AreEqual(3, ProgressPrinter.CountVisualLines(multiLine, 80));
    }

    [TestMethod]
    public void ThreadProgressSnapshotFormatsIdleCorrectly() {
      var snapshot = new ThreadProgressSnapshot {
        ThreadIndex = 1,
        Operation = ThreadOperation.Idle,
        CurrentPath = null,
        BytesCopied = 0,
        TotalBytes = 0,
        Elapsed = TimeSpan.Zero
      };
      Assert.AreEqual("Thread  1: idle", snapshot.Format());
    }

    [TestMethod]
    public void ThreadProgressSnapshotFormatsCopyingCorrectly() {
      var sourceRoot = new FullPath("/path/to");
      var snapshot = new ThreadProgressSnapshot {
        ThreadIndex = 2,
        Operation = ThreadOperation.CopyingFile,
        CurrentPath = sourceRoot.Combine("file.dat"),
        BytesCopied = 10 * 1024 * 1024,
        TotalBytes = 50 * 1024 * 1024,
        Elapsed = TimeSpan.FromSeconds(1.25)
      };
      // Without root
      var formatted = snapshot.Format();
      StringAssert.Contains(formatted, "Thread  2: Copying /path/to/file.dat (10.0 MB / 50.0 MB, 1.25s)");

      // With root
      var formattedWithRoot = snapshot.Format(sourceRoot);
      StringAssert.Contains(formattedWithRoot, "Thread  2: Copying file.dat (10.0 MB / 50.0 MB, 1.25s)");
    }

    [TestMethod]
    public void ThreadProgressSnapshotFormatsTraversingCorrectly() {
      var sourceRoot = new FullPath("/path");
      var snapshot = new ThreadProgressSnapshot {
        ThreadIndex = 3,
        Operation = ThreadOperation.TraversingDirectory,
        CurrentPath = sourceRoot.Combine("to").Combine("dir"),
        BytesCopied = 0,
        TotalBytes = 0,
        Elapsed = TimeSpan.FromSeconds(0.5)
      };
      var formattedWithRoot = snapshot.Format(sourceRoot);
      char sep = OperatingSystem.IsWindows() ? '\\' : '/';
      StringAssert.Contains(formattedWithRoot, $"Thread  3: Traversing to{sep}dir (0.50s)");
    }

    [TestMethod]
    public void ThreadProgressSnapshotFormatsDeletingCorrectly() {
      var destRoot = new FullPath("/dest/dir");
      var snapshot = new ThreadProgressSnapshot {
        ThreadIndex = 4,
        Operation = ThreadOperation.DeletingEntry,
        CurrentPath = destRoot.Combine("old.txt"),
        BytesCopied = 0,
        TotalBytes = 0,
        Elapsed = TimeSpan.FromSeconds(0.1)
      };
      var formattedWithRoot = snapshot.Format(destinationPath: destRoot);
      StringAssert.Contains(formattedWithRoot, "Thread  4: Deleting old.txt (0.10s)");
    }

    [TestMethod]
    public void ThreadProgressTrackerMultiThreadedRegistration() {
      var tracker = new ThreadProgressTracker();
      var threads = new List<Thread>();

      for (int i = 0; i < 4; i++) {
        var thread = new Thread(() => {
          var state = tracker.Current;
          Assert.IsNotNull(state);
          Assert.IsTrue(state.ThreadIndex >= 1 && state.ThreadIndex <= 4);
        });
        threads.Add(thread);
        thread.Start();
      }

      foreach (var thread in threads) {
        thread.Join();
      }

      var states = tracker.GetAllStates();
      Assert.AreEqual(4, states.Count);
      for (int i = 0; i < 4; i++) {
        Assert.AreEqual(i + 1, states[i].ThreadIndex);
      }

      var lines = tracker.GetFormattedLines();
      Assert.AreEqual(4, lines.Count);
      foreach (var line in lines) {
        StringAssert.Contains(line, "idle");
      }
    }

    [TestMethod]
    public void ThreadProgressTrackerTracksActiveOperations() {
      var tracker = new ThreadProgressTracker();
      var state = tracker.Current;

      var dirData = new FileSystemEntryData(FileAttributes.Directory, 0, DateTime.UtcNow.ToFileTimeUtc());
      var dirEntry = new FileSystemEntry(new FullPath("/source/myfolder"), dirData);

      state.SetTraversing(dirEntry);
      Assert.AreEqual(ThreadOperation.TraversingDirectory, state.Operation);
      StringAssert.Contains(tracker.GetFormattedLines()[0], "Traversing /source/myfolder");

      var fileData = new FileSystemEntryData(FileAttributes.Normal, 50 * 1024 * 1024, DateTime.UtcNow.ToFileTimeUtc());
      var fileEntry = new FileSystemEntry(new FullPath("/source/myfolder/large.bin"), fileData);

      state.SetCopying(fileEntry);
      Assert.AreEqual(ThreadOperation.CopyingFile, state.Operation);
      StringAssert.Contains(tracker.GetFormattedLines()[0], "Copying /source/myfolder/large.bin (0 B / 50.0 MB");

      state.UpdateCopyProgress(25 * 1024 * 1024);
      StringAssert.Contains(tracker.GetFormattedLines()[0], "Copying /source/myfolder/large.bin (25.0 MB / 50.0 MB");

      state.SetDeleting(fileEntry);
      Assert.AreEqual(ThreadOperation.DeletingEntry, state.Operation);
      StringAssert.Contains(tracker.GetFormattedLines()[0], "Deleting /source/myfolder/large.bin");

      state.SetIdle();
      Assert.AreEqual(ThreadOperation.Idle, state.Operation);
      StringAssert.Contains(tracker.GetFormattedLines()[0], "idle");
    }

    [TestMethod]
    public void ThreadProgressTrackerFormatsRelativePaths() {
      var tracker = new ThreadProgressTracker();
      var sourceRoot = new FullPath("/source/myfolder");
      var destRoot = new FullPath("/dest/backup");
      tracker.SourcePath = sourceRoot;
      tracker.DestinationPath = destRoot;

      var state = tracker.Current;

      // File under source
      var fileData = new FileSystemEntryData(FileAttributes.Normal, 100 * 1024, DateTime.UtcNow.ToFileTimeUtc());
      var fileEntry = new FileSystemEntry(sourceRoot.Combine("sub").Combine("code.cs"), fileData);

      state.SetCopying(fileEntry);
      state.UpdateCopyProgress(50 * 1024);
      char sep = OperatingSystem.IsWindows() ? '\\' : '/';
      var lines = tracker.GetFormattedLines();
      StringAssert.Contains(lines[0], $"Copying sub{sep}code.cs (50.0 KB / 100.0 KB");

      // Root itself
      var rootData = new FileSystemEntryData(FileAttributes.Directory, 0, DateTime.UtcNow.ToFileTimeUtc());
      var rootEntry = new FileSystemEntry(sourceRoot, rootData);
      state.SetTraversing(rootEntry);
      lines = tracker.GetFormattedLines();
      StringAssert.Contains(lines[0], "Traversing .");

      // Entry under destination
      var destFileEntry = new FileSystemEntry(destRoot.Combine("extra.bak"), fileData);
      state.SetDeleting(destFileEntry);
      lines = tracker.GetFormattedLines();
      StringAssert.Contains(lines[0], "Deleting extra.bak");
    }

    [TestMethod]
    public void ProgressMonitorTracksTotalSizeAccuratelyWithChunks() {
      var monitor = new CopyProgressMonitor();
      monitor.Start();

      var data = new FileSystemEntryData(FileAttributes.Normal, 3 * 1024 * 1024, DateTime.UtcNow.ToFileTimeUtc());
      var fileEntry = new FileSystemEntry(new FullPath("/test/file.dat"), data);

      monitor.OnFileCopying(fileEntry);
      monitor.OnFileCopyingProgress(fileEntry, TimeSpan.FromMilliseconds(50), 1 * 1024 * 1024);
      monitor.OnFileCopyingProgress(fileEntry, TimeSpan.FromMilliseconds(100), 2 * 1024 * 1024);
      monitor.OnFileCopyingProgress(fileEntry, TimeSpan.FromMilliseconds(150), 3 * 1024 * 1024);
      monitor.OnFileCopied(fileEntry, TimeSpan.FromMilliseconds(150), 3 * 1024 * 1024);

      monitor.Stop();
      var stats = monitor.GetStatistics();

      Assert.AreEqual(1, stats.FileCopiedCount);
      Assert.AreEqual(3 * 1024 * 1024, stats.FileCopiedTotalSize);
    }
  }
}
