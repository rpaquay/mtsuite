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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using mtsuite.CoreFileSystem;

namespace mtsuite.shared {
  public abstract class ProgressMonitor<TStatistics> : IProgressMonitor<TStatistics> where TStatistics : Statistics, new() {
    private readonly ProgressPrinter _printer = new ProgressPrinter();
    private readonly ThreadProgressTracker _threadTracker = new ThreadProgressTracker();
    private readonly Stopwatch _stopWatch = new Stopwatch();
    private readonly Stopwatch _displayTimer = new Stopwatch();
    private readonly ConcurrentQueue<Exception> _errors = new ConcurrentQueue<Exception>();
    private readonly ConcurrentQueue<Exception> _warnings = new ConcurrentQueue<Exception>();

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

    private long _fileCloneCount;
    private long _fileCloneTotalSize;
    private long _fileAlreadyClonedCount;
    private long _fileAlreadyClonedTotalSize;
    private long _fileCloneSkippedCount;
    private long _fileCloneSkippedTotalSize;

    public FullPath? SourcePath {
      get => _threadTracker.SourcePath;
      set => _threadTracker.SourcePath = value;
    }

    public FullPath? DestinationPath {
      get => _threadTracker.DestinationPath;
      set => _threadTracker.DestinationPath = value;
    }

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

    public TStatistics GetStatistics() {
      var stats = new TStatistics();
      FillInStatistics(stats);
      return stats;
    }

    protected virtual void FillInStatistics(TStatistics statistics) {
      statistics.ElapsedTime = _stopWatch.Elapsed;
      statistics.TotalProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
      statistics.DirectoryEnumeratedCount = _directoryEnumeratedCount;
      statistics.FileEnumeratedCount = _fileEnumeratedCount;
      statistics.SymlinkEnumeratedCount = _symlinkEnumeratedCount;
      statistics.FileEnumeratedTotalSize = _fileEnumeratedTotalSize;
      statistics.DirectoryToDeleteCount = _directoryToDeleteCount;
      statistics.FileToDeleteCount = _fileToDeleteCount;
      statistics.DirectoryTraversedCount = _directoryTraversedCount;
      statistics.FileCopiedCount = _fileCopiedCount;
      statistics.SymlinkCopiedCount = _symlinkCopiedCount;
      statistics.FileCopiedTotalSize = _fileCopiedTotalSize;
      statistics.DirectoryDeletedCount = _directoryDeletedCount;
      statistics.FileDeletedCount = _fileDeletedCount;
      statistics.SymlinkDeletedCount = _symlinkDeletedCount;
      statistics.FileDeletedTotalSize = _fileDeletedTotalSize;
      statistics.DirectoryCreatedCount = _directoryCreatedCount;
      statistics.FileSkippedCount = _fileSkippedCount;
      statistics.SymlinkSkippedCount = _symlinkSkippedCount;
      statistics.FileSkippedTotalSize = _fileSkippedTotalSize;
      statistics.FileClonedCount = _fileCloneCount;
      statistics.FileClonedTotalSize = _fileCloneTotalSize;
      statistics.FileAlreadyClonedCount = _fileAlreadyClonedCount;
      statistics.FileAlreadyClonedTotalSize = _fileAlreadyClonedTotalSize;
      statistics.FileCloneSkippedCount = _fileCloneSkippedCount;
      statistics.FileCloneSkippedTotalSize = _fileCloneSkippedTotalSize;
      statistics.Errors = _errors;
      statistics.Warnings = _warnings;
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
      var diskSize = 0L;
      foreach (var entry in entries) {
        // Note: Order is important (symlink first)
        if (entry.IsReparsePoint) symlinkCount++;
        else if (entry.IsDirectory) directoryCount++;
        else if (entry.IsFile) {
          fileCount++;
          diskSize += entry.FileSize;
        }
      }
      Interlocked.Add(ref _directoryEnumeratedCount, directoryCount);
      Interlocked.Add(ref _fileEnumeratedCount, fileCount);
      Interlocked.Add(ref _symlinkEnumeratedCount, symlinkCount);
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
      _threadTracker.Current.SetTraversing(directory);
    }

    public virtual void OnDirectoryTraversed(FileSystemEntry directory) {
      _threadTracker.Current.SetIdle();
      Interlocked.Increment(ref _directoryTraversedCount);
    }

    public virtual void OnDirectoryCreated(FileSystemEntry directory) {
      Interlocked.Increment(ref _directoryCreatedCount);
    }

    public virtual void OnEntryDeleting(FileSystemEntry entry) {
      _threadTracker.Current.SetDeleting(entry);
    }

    public virtual void OnEntryDeleted(FileSystemEntry entry, TimeSpan elapsed) {
      _threadTracker.Current.SetIdle();
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
      _threadTracker.Current.SetIdle();
      if (entry.IsReparsePoint) {
        Interlocked.Increment(ref _symlinkSkippedCount);
      } else if (entry.IsFile) {
        Interlocked.Increment(ref _fileSkippedCount);
        Interlocked.Add(ref _fileSkippedTotalSize, size);
      }
    }

    public virtual void OnFileComparing(FileSystemEntry entry) {
      _threadTracker.Current.SetComparing(entry);
      Pulse();
    }

    public virtual void OnFileComparingProgress(FileSystemEntry entry, TimeSpan elapsed, long bytesFromPreviousCall, long bytesSoFar) {
      _threadTracker.Current.UpdateCompareProgress(bytesSoFar);
      Pulse();
    }

    public virtual void OnFileCompared(FileSystemEntry entry, TimeSpan elapsed, long bytesTotal) {
      _threadTracker.Current.SetIdle();
      Pulse();
    }

    public virtual void OnFileCopying(FileSystemEntry entry) {
      _threadTracker.Current.SetCopying(entry);
    }

    public virtual void OnFileCopyingProgress(FileSystemEntry entry, TimeSpan elapsed, long bytesFromPreviousCall, long bytesSoFar) {
      _threadTracker.Current.UpdateCopyProgress(bytesSoFar);
      Interlocked.Add(ref _fileCopiedTotalSize, bytesFromPreviousCall);
      Pulse();
    }

    public virtual void OnFileCopied(FileSystemEntry entry, TimeSpan elapsed, long bytesTotal) {
      _threadTracker.Current.SetIdle();
      if (entry.IsReparsePoint) {
        Interlocked.Increment(ref _symlinkCopiedCount);
      } else if (entry.IsFile) {
        Interlocked.Increment(ref _fileCopiedCount);
      }
      Pulse();
    }

    public virtual void OnFileCloning(FileSystemEntry entry) {
      _threadTracker.Current.SetCloning(entry);
    }

    public virtual void OnFileCloned(FileSystemEntry entry, TimeSpan elapsed, long bytesTotal) {
      _threadTracker.Current.SetIdle();
      Interlocked.Increment(ref _fileCloneCount);
      Interlocked.Add(ref _fileCloneTotalSize, bytesTotal);
      Pulse();
    }

    public virtual void OnFileCloneSkipped(FileSystemEntry entry, long bytes) {
      _threadTracker.Current.SetIdle();
      Interlocked.Increment(ref _fileCloneSkippedCount);
      Interlocked.Add(ref _fileCloneSkippedTotalSize, bytes);
      Pulse();
    }

    public virtual void OnFileAlreadyCloned(FileSystemEntry entry, long bytes) {
      _threadTracker.Current.SetIdle();
      Interlocked.Increment(ref _fileAlreadyClonedCount);
      Interlocked.Add(ref _fileAlreadyClonedTotalSize, bytes);
      Pulse();
    }

    public virtual void OnError(FullPath path, Exception e) {
      if (IsWarning(path, e)) {
        _warnings.Enqueue(e);
      } else {
        _errors.Enqueue(e);
      }
      Pulse();
    }

    public virtual bool IsWarning(FullPath path, Exception e) {
      return false;
    }

    protected abstract void DisplayStatus(TStatistics statistics);

    protected IReadOnlyList<string> GetThreadProgressLines() {
      return _threadTracker.GetFormattedLines();
    }

    protected virtual void Print(ICollection<PrinterEntry> fields) {
      _printer.Print(fields);
    }

    protected virtual void Print(ICollection<PrinterEntry> fields, IReadOnlyList<string>? additionalLines) {
      _printer.Print(fields, additionalLines);
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