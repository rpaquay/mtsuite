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
using mtsuite.shared.Files;

namespace mtsuite.shared {
  public abstract class ProgressMonitor : IProgressMonitor {
    private readonly ProgressPrinter _printer = new ProgressPrinter();
    private readonly Stopwatch _stopWatch = new Stopwatch();
    private readonly Stopwatch _displayTimer = new Stopwatch();
    private readonly List<Exception> _errors = new List<Exception>();
    private readonly object _threadElapsedTimeLock = new object();
    private TimeSpan _threadElapsedTime;

    private long _directoryEnumeratedCount;
    private long _fileEnumeratedCount;

    private long _directoryTraversedCount;
    private long _fileCopiedCount;
    private long _symlinkCopiedCount;
    private long _fileCopiedTotalSize;

    private long _fileProcessedCount;
    private long _symlinkProcessedCount;
    private long _fileProcessedTotalSize;

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
        ThreadElapsedTime = _threadElapsedTime,

        DirectoryEnumeratedCount = _directoryEnumeratedCount,
        FileEnumeratedCount = _fileEnumeratedCount,

        DirectoryToDeleteCount = _directoryToDeleteCount,
        FileToDeleteCount = _fileToDeleteCount,

        DirectoryTraversedCount = _directoryTraversedCount,

        FileProcessedCount = _fileProcessedCount,
        SymlinkProcessedCount = _symlinkProcessedCount,
        FileProcessedTotalSize = _fileProcessedTotalSize,

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

    public void OnEntriesDiscovered(FileSystemEntry directory, List<FileSystemEntry> entries) {
      var count = CountPair(entries,
        x => x.IsFile || x.IsReparsePoint, // Real files or any kind of reparse point
        x => x.IsDirectory && !x.IsReparsePoint); // Real directories only
      Interlocked.Add(ref _fileEnumeratedCount, count.Key);
      Interlocked.Add(ref _directoryEnumeratedCount, count.Value);
      Pulse();
    }

    public void OnEntriesToDeleteDiscovered(FileSystemEntry directory, List<FileSystemEntry> entries) {
      var count = CountPair(entries,
        x => x.IsFile || x.IsReparsePoint, // Real files or any kind of reparse point
        x => x.IsDirectory && !x.IsReparsePoint); // Real directories only
      Interlocked.Add(ref _fileToDeleteCount, count.Key);
      Interlocked.Add(ref _directoryToDeleteCount, count.Value);
      Pulse();
    }

    public void OnEntriesProcessed(FileSystemEntry directory, List<FileSystemEntry> entries) {
      var count = CountPair(entries,
        x => x.IsReparsePoint, // Any kind of reparse point
        x => x.IsFile && !x.IsReparsePoint); // Real files only
      Interlocked.Add(ref _symlinkProcessedCount, count.Key);
      Interlocked.Add(ref _fileProcessedCount, count.Value);
      var diskSize = entries
        .Where(x => x.IsFile && !x.IsReparsePoint) // Real files only
        .Aggregate(0L, (size, entry) => size + entry.FileSize);
      Interlocked.Add(ref _fileProcessedTotalSize, diskSize);
    }

    public void OnDirectoryTraversing(FileSystemEntry directory) {
    }

    public void OnDirectoryTraversed(FileSystemEntry directory) {
      Interlocked.Increment(ref _directoryTraversedCount);
    }

    public void OnDirectoryCreated(FileSystemEntry directory) {
      Interlocked.Increment(ref _directoryCreatedCount);
    }

    public void OnEntryDeleting(Stopwatch stopwatch, FileSystemEntry entry) {
      stopwatch.Restart();
    }

    public void OnEntryDeleted(Stopwatch stopwatch, FileSystemEntry entry) {
      stopwatch.Stop();
      lock (_threadElapsedTimeLock) {
        _threadElapsedTime += stopwatch.Elapsed;
      }

      if (entry.IsReparsePoint) {
        Interlocked.Increment(ref _symlinkDeletedCount);
      } else if (entry.IsFile) {
        Interlocked.Increment(ref _fileDeletedCount);
        Interlocked.Add(ref _fileDeletedTotalSize, entry.FileSize);
      } else if (entry.IsDirectory) {
        Interlocked.Increment(ref _directoryDeletedCount);
      }
    }

    public void OnFileSkipped(FileSystemEntry entry, long size) {
      if (entry.IsReparsePoint) {
        Interlocked.Increment(ref _symlinkSkippedCount);
      } else if (entry.IsFile) {
        Interlocked.Increment(ref _fileSkippedCount);
        Interlocked.Add(ref _fileSkippedTotalSize, size);
      }
    }

    public void OnFileCopying(Stopwatch stopwatch, FileSystemEntry entry) {
      stopwatch.Restart();
    }

    public void OnFileCopyingProgress(Stopwatch stopwatch, FileSystemEntry entry, long size) {
      Interlocked.Add(ref _fileCopiedTotalSize, size);
      Pulse();
    }

    public void OnFileCopied(Stopwatch stopwatch, FileSystemEntry entry) {
      stopwatch.Stop();

      lock (_threadElapsedTimeLock) {
        _threadElapsedTime += stopwatch.Elapsed;
      }

      if (entry.IsReparsePoint) {
        Interlocked.Increment(ref _symlinkCopiedCount);
      } else if (entry.IsFile) {
        Interlocked.Increment(ref _fileCopiedCount);
      }

      Pulse();
    }

    public void OnError(Exception e) {
      lock (_errors) {
        _errors.Add(e);
      }

      Pulse();
    }

    protected abstract void DisplayStatus(Statistics statistics);

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

    protected void Print(IEnumerable<KeyValuePair<string, string>> fields) {
      _printer.Print(fields);
    }
  }
}