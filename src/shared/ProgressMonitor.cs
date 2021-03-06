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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using mtsuite.CoreFileSystem;

namespace mtsuite.shared {
  public abstract class ProgressMonitor : IProgressMonitor {
    private readonly ProgressPrinter _printer = new ProgressPrinter();
    private readonly Stopwatch _stopWatch = new Stopwatch();
    private readonly Stopwatch _displayTimer = new Stopwatch();
    private readonly List<Exception> _errors = new List<Exception>();

    private long _directoryEnumeratedCount;
    private long _fileEnumeratedCount;
    private long _symlinkEnumeratedCount;
    private long _fileEnumeratedTotalSize;

    private long _directoryTraversedCount;
    private long _fileCopiedCount;
    private long _symlinkCopiedCount;
    private long _fileCopiedTotalSize;

    private long _directoryToDeleteCount;
    private long _fileToDeleteCount;

    private long _directoryDeletedCount;
    private long _fileDeletedCount;
    private long _symlinkDeletedCount;
    private long _fileDeletedTotalSize;

    private long _directoryCreatedCount;

    private long _fileSkippedCount;
    private long _symlinkSkippedCount;
    private long _fileSkippedTotalSize;

    public void Start() {
      _stopWatch.Restart();
      _displayTimer.Restart();
    }

    public void Pulse() {
      if (IsTimeToDisplayStatus()) {
        DisplayStatus(GetStatistics());
      }
    }

    public void Stop() {
      _stopWatch.Stop();
      _displayTimer.Stop();
      DisplayStatus(GetStatistics());
      _printer.Stop();
    }

    public Statistics GetStatistics() {
      return new Statistics {
        ElapsedTime = _stopWatch.Elapsed,
        TotalProcessorTime = Process.GetCurrentProcess().TotalProcessorTime,

        DirectoryEnumeratedCount = _directoryEnumeratedCount,
        FileEnumeratedCount = _fileEnumeratedCount,
        SymlinkEnumeratedCount = _symlinkEnumeratedCount,
        FileEnumeratedTotalSize = _fileEnumeratedTotalSize,

        DirectoryToDeleteCount = _directoryToDeleteCount,
        FileToDeleteCount = _fileToDeleteCount,

        DirectoryTraversedCount = _directoryTraversedCount,

        FileCopiedCount = _fileCopiedCount,
        SymlinkCopiedCount = _symlinkCopiedCount,
        FileCopiedTotalSize = _fileCopiedTotalSize,

        DirectoryDeletedCount = _directoryDeletedCount,
        FileDeletedCount = _fileDeletedCount,
        SymlinkDeletedCount = _symlinkDeletedCount,
        FileDeletedTotalSize = _fileDeletedTotalSize,

        DirectoryCreatedCount = _directoryCreatedCount,

        FileSkippedCount = _fileSkippedCount,
        SymlinkSkippedCount = _symlinkSkippedCount,
        FileSkippedTotalSize = _fileSkippedTotalSize,

        Errors = _errors
      };
    }

    private KeyValuePair<int, int> CountPair<T>(List<T> list, Func<T, bool> pred1, Func<T, bool> pred2) {
      var count1 = 0;
      var count2 = 0;
      foreach (var x in list) {
        if (pred1(x)) count1++;
        if (pred2(x)) count2++;
      }
      return new KeyValuePair<int, int>(count1, count2);
    }

    public virtual void OnEntriesDiscovered(FileSystemEntry directory, List<FileSystemEntry> entries) {
      var directoryCount = 0;
      var fileCount = 0;
      var symlinkCount = 0;
      foreach(var entry in entries) {
        // Note: Order is important (symlink first)
        if (entry.IsReparsePoint) symlinkCount++;
        else if (entry.IsDirectory) directoryCount++;
        else if (entry.IsFile) fileCount++;
      }
      Interlocked.Add(ref _directoryEnumeratedCount, directoryCount);
      Interlocked.Add(ref _fileEnumeratedCount, fileCount);
      Interlocked.Add(ref _symlinkEnumeratedCount, symlinkCount);
      var diskSize = entries
        .Where(x => x.IsFile && !x.IsReparsePoint) // Real files only
        .Aggregate(0L, (size, entry) => size + entry.FileSize);
      Interlocked.Add(ref _fileEnumeratedTotalSize, diskSize);
      Pulse();
    }

    public virtual void OnEntriesToDeleteDiscovered(FileSystemEntry directory, List<FileSystemEntry> entries) {
      var count = CountPair(entries,
        x => x.IsFile || x.IsReparsePoint, // Real files or any kind of reparse point
        x => x.IsDirectory && !x.IsReparsePoint); // Real directories only
      Interlocked.Add(ref _fileToDeleteCount, count.Key);
      Interlocked.Add(ref _directoryToDeleteCount, count.Value);
      Pulse();
    }

    public virtual void OnDirectoryTraversing(FileSystemEntry directory) {
    }

    public virtual void OnDirectoryTraversed(FileSystemEntry directory) {
      Interlocked.Increment(ref _directoryTraversedCount);
    }

    public virtual void OnDirectoryCreated(FileSystemEntry directory) {
      Interlocked.Increment(ref _directoryCreatedCount);
    }

    public virtual void OnEntryDeleting(Stopwatch stopwatch, FileSystemEntry entry) {
      stopwatch.Restart();
    }

    public virtual void OnEntryDeleted(Stopwatch stopwatch, FileSystemEntry entry) {
      stopwatch.Stop();

      if (entry.IsReparsePoint) {
        Interlocked.Increment(ref _symlinkDeletedCount);
      } else if (entry.IsFile) {
        Interlocked.Increment(ref _fileDeletedCount);
        Interlocked.Add(ref _fileDeletedTotalSize, entry.FileSize);
      } else if (entry.IsDirectory) {
        Interlocked.Increment(ref _directoryDeletedCount);
      }
    }

    public virtual void OnFileSkipped(FileSystemEntry entry, long size) {
      if (entry.IsReparsePoint) {
        Interlocked.Increment(ref _symlinkSkippedCount);
      } else if (entry.IsFile) {
        Interlocked.Increment(ref _fileSkippedCount);
        Interlocked.Add(ref _fileSkippedTotalSize, size);
      }
    }

    public virtual void OnFileCopying(Stopwatch stopwatch, FileSystemEntry entry) {
      stopwatch.Restart();
    }

    public virtual void OnFileCopyingProgress(Stopwatch stopwatch, FileSystemEntry entry, long size) {
      Interlocked.Add(ref _fileCopiedTotalSize, size);
      Pulse();
    }

    public virtual void OnFileCopied(Stopwatch stopwatch, FileSystemEntry entry) {
      stopwatch.Stop();

      if (entry.IsReparsePoint) {
        Interlocked.Increment(ref _symlinkCopiedCount);
      } else if (entry.IsFile) {
        Interlocked.Increment(ref _fileCopiedCount);
      }

      Pulse();
    }

    public virtual void OnError(Exception e) {
      lock (_errors) {
        _errors.Add(e);
      }

      Pulse();
    }

    protected abstract void DisplayStatus(Statistics statistics);

    protected virtual void Print(ICollection<PrinterEntry> fields) {
      _printer.Print(fields);
    }

    private bool IsTimeToDisplayStatus() {
      var displayStatus = false;
      if (_displayTimer.ElapsedMilliseconds >= 250) {
        lock (_displayTimer) {
          if (_displayTimer.ElapsedMilliseconds >= 250) {
            displayStatus = true;
            _displayTimer.Restart();
          }
        }
      }
      return displayStatus;
    }
  }
}