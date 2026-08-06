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
#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using mtsuite.CoreFileSystem;
using mtsuite.shared.Utils;

namespace mtsuite.shared {
  public enum ThreadOperation {
    Idle,
    TraversingDirectory,
    CopyingFile,
    DeletingEntry
  }

  /// <summary>
  /// Immutable snapshot of a worker thread's progress state at sampling time.
  /// </summary>
  /// <remarks>
  /// Performance considerations:
  /// - Stored as a readonly struct to avoid GC allocations during periodic (0.25s) state collection.
  /// - Holds a reference to <see cref="FullPath"/> instead of an eager formatted string, deferring any string
  ///   allocation and path prefix stripping exclusively to <see cref="Format()"/> when rendering console output.
  /// </remarks>
  public readonly struct ThreadProgressSnapshot {
    public int ThreadIndex { get; init; }
    public ThreadOperation Operation { get; init; }
    public FullPath? CurrentPath { get; init; }
    public long BytesCopied { get; init; }
    public long TotalBytes { get; init; }
    public TimeSpan Elapsed { get; init; }

    public string Format(FullPath? sourcePath = null, FullPath? destinationPath = null) {
      if (Operation == ThreadOperation.Idle || CurrentPath == null) {
        return $"Thread {ThreadIndex,2}: idle";
      }

      string pathText;
      if (sourcePath != null && CurrentPath.TryGetRelativePath(sourcePath, out var relSource)) {
        pathText = relSource;
      } else if (destinationPath != null && CurrentPath.TryGetRelativePath(destinationPath, out var relDest)) {
        pathText = relDest;
      } else {
        pathText = PathHelpers.StripLongPathPrefix(CurrentPath.FullName);
      }

      var elapsedText = FormatHelpers.FormatElapsedTime(Elapsed);
      switch (Operation) {
        case ThreadOperation.CopyingFile:
          var sizeText = TotalBytes > 0
            ? $"{FormatHelpers.FormatSize(BytesCopied)} / {FormatHelpers.FormatSize(TotalBytes)}"
            : FormatHelpers.FormatSize(BytesCopied);
          return $"Thread {ThreadIndex,2}: Copying {pathText} ({sizeText}, {elapsedText})";

        case ThreadOperation.TraversingDirectory:
          return $"Thread {ThreadIndex,2}: Traversing {pathText} ({elapsedText})";

        case ThreadOperation.DeletingEntry:
          return $"Thread {ThreadIndex,2}: Deleting {pathText} ({elapsedText})";

        default:
          return $"Thread {ThreadIndex,2}: idle";
      }
    }
  }

  /// <summary>
  /// Tracks the real-time progress state for a single worker thread.
  /// </summary>
  /// <remarks>
  /// Performance considerations:
  /// - Instance is stored per-thread using <see cref="ThreadLocal{T}"/> in <see cref="ThreadProgressTracker"/>,
  ///   ensuring worker threads write exclusively to their own slot with zero lock contention.
  /// - State updates during hot file copying/traversal paths involve only simple field assignments,
  ///   timestamp capturing, and passing existing references (<see cref="FullPath"/>), resulting in zero
  ///   heap allocations and negligible runtime overhead (nanoseconds per operation).
  /// - Reads during snapshot creation are lock-free, using <see cref="Volatile.Read(ref long)"/> for atomic byte counts.
  /// </remarks>
  public class ThreadProgressState {
    public int ThreadIndex { get; }
    public int ManagedThreadId { get; }

    public volatile ThreadOperation Operation;
    public volatile FullPath? CurrentPath;
    public long BytesCopied;
    public long TotalBytes;
    public long StartTimestamp;
    public long PreviousBytes;

    public ThreadProgressState(int threadIndex, int managedThreadId) {
      ThreadIndex = threadIndex;
      ManagedThreadId = managedThreadId;
      Operation = ThreadOperation.Idle;
    }

    public void SetTraversing(FileSystemEntry directory) {
      CurrentPath = directory.Path;
      StartTimestamp = Stopwatch.GetTimestamp();
      BytesCopied = 0;
      TotalBytes = 0;
      PreviousBytes = 0;
      Operation = ThreadOperation.TraversingDirectory;
    }

    public void SetCopying(FileSystemEntry file) {
      CurrentPath = file.Path;
      StartTimestamp = Stopwatch.GetTimestamp();
      BytesCopied = 0;
      TotalBytes = file.FileSize;
      PreviousBytes = 0;
      Operation = ThreadOperation.CopyingFile;
    }

    public void UpdateCopyProgress(long bytesCopied) {
      BytesCopied = bytesCopied;
    }

    public void SetDeleting(FileSystemEntry entry) {
      CurrentPath = entry.Path;
      StartTimestamp = Stopwatch.GetTimestamp();
      BytesCopied = 0;
      TotalBytes = 0;
      PreviousBytes = 0;
      Operation = ThreadOperation.DeletingEntry;
    }

    public void SetIdle() {
      Operation = ThreadOperation.Idle;
      CurrentPath = null;
      StartTimestamp = 0;
      BytesCopied = 0;
      TotalBytes = 0;
      PreviousBytes = 0;
    }

    public ThreadProgressSnapshot CreateSnapshot() {
      var op = Operation;
      var path = CurrentPath;
      var bytes = Volatile.Read(ref BytesCopied);
      var total = TotalBytes;
      var startTs = StartTimestamp;
      var elapsed = (startTs != 0 && op != ThreadOperation.Idle)
        ? Stopwatch.GetElapsedTime(startTs)
        : TimeSpan.Zero;

      return new ThreadProgressSnapshot {
        ThreadIndex = ThreadIndex,
        Operation = op,
        CurrentPath = path,
        BytesCopied = bytes,
        TotalBytes = total,
        Elapsed = elapsed
      };
    }
  }
}
