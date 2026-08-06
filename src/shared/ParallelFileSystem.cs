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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using mtsuite.shared.Collections;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtsuite.shared {
  public interface INoAllocStopwatchFactory {
    NoAllocStopwatch Create();
    
    TimeSpan GetElapsed(long startingTimestamp);
  }

  public class NoAllocStopwatchFactory : INoAllocStopwatchFactory {
    public static readonly NoAllocStopwatchFactory Instance = new NoAllocStopwatchFactory();

    public TimeSpan GetElapsed(long startingTimestamp) {
      return Stopwatch.GetElapsedTime(startingTimestamp);
    }

    public NoAllocStopwatch Create() {
      return new NoAllocStopwatch(this, Stopwatch.GetTimestamp());
    }
  }

  public readonly struct NoAllocStopwatch(INoAllocStopwatchFactory factory, long startingTimestamp) {
    public TimeSpan Elapsed => factory.GetElapsed(startingTimestamp);
  }

  public class ParallelFileSystem : IParallelFileSystem {
    private readonly IFileSystem _fileSystem;
    private readonly INoAllocStopwatchFactory _stopwatchFactory;
    private readonly IPool<List<FileSystemEntry>> _entryListPool = new ListPool<FileSystemEntry>();
    private readonly IPool<List<Task>> _taskListPool = new ListPool<Task>();

    private readonly IPool<SmallSet<FileSystemEntry>> _entrySetPool =
      PoolFactory<SmallSet<FileSystemEntry>>.Create(
        () => new SmallSet<FileSystemEntry>(FileSystemEntryNameComparer.Instance),
        x => x.Clear());

    private readonly IPool<Dictionary<string, FileSystemEntry>> _sourceDictPool =
      PoolFactory<Dictionary<string, FileSystemEntry>>.Create(
        () => new Dictionary<string, FileSystemEntry>(256, PathHelpers.FileNameComparer),
        d => d.Clear());

    /// <summary>
    /// Callback to <see cref="IFileSystem.CopyFile"/>, stored in a field to avoid GC allocation
    /// at every invocation.
    /// </summary>
    private readonly CopyFileCallback<CopyFileData> _copyFileCallback = static (ref FileSystemEntry sourceEntry, long copiedBytes, long totalBytes, ref CopyFileData data) => {
      data.Instance.OnFileCopyingProgress(sourceEntry, data.Stopwatch.Elapsed, copiedBytes);
    };

    public ParallelFileSystem(
      IFileSystem fileSystem,
      INoAllocStopwatchFactory? stopwatchFactory = null,
      bool parallelFileCopy = false) {
      _fileSystem = fileSystem;
      _stopwatchFactory = stopwatchFactory ?? NoAllocStopwatchFactory.Instance;
      ParallelFileCopy = parallelFileCopy;
    }

    public bool ParallelFileCopy { get; set; }

    /// <summary>
    /// Default file size threshold (10 MB) above which file copying is offloaded to a background task.
    /// </summary>
    public const long DefaultLargeFileAsyncThreshold = 10 * 1024 * 1024;

    /// <summary>
    /// File size threshold in bytes above which file copying is offloaded to a background task
    /// to avoid blocking directory traversal and other concurrent operations.
    /// </summary>
    public long LargeFileAsyncThreshold { get; set; } = DefaultLargeFileAsyncThreshold;

    public event Action<FullPath, Exception>? Error;
    public event Action? Pulse;
    public event Action<FileSystemEntry>? EntriesDiscovering;
    public event Action<FileSystemEntry, List<FileSystemEntry>>? EntriesDiscovered;
    public event Action<FileSystemEntry>? EntriesToDeleteDiscovering;
    public event Action<FileSystemEntry, List<FileSystemEntry>>? EntriesToDeleteDiscovered;
    public event Action<FileSystemEntry, List<FileSystemEntry>>? EntriesToDeleteProcessed;
    public event Action<FileSystemEntry>? EntryDeleting;
    public event Action<FileSystemEntry, TimeSpan>? EntryDeleted;
    public event Action<FileSystemEntry>? FileCopySkipped;
    public event Action<FileSystemEntry>? FileCopying;
    public event Action<FileSystemEntry, TimeSpan, long>? FileCopyingProgress;
    public event Action<FileSystemEntry, TimeSpan, long>? FileCopied;
    public event Action<FileSystemEntry>? FileCompacting;
    public event Action<FileSystemEntry, TimeSpan, long>? FileCompacted;
    public event Action<FileSystemEntry>? FileCompactSkipped;
    public event Action<FileSystemEntry>? DirectoryTraversing;
    public event Action<FileSystemEntry>? DirectoryTraversed;
    public event Action<FileSystemEntry>? DirectoryCreated;

    private FromPool<List<FileSystemEntry>> GetDirectoryEntries(FullPath directoryPath) {
      TryGetDirectoryEntries(directoryPath, out var entries);
      return entries;
    }

    private bool TryGetDirectoryEntries(FullPath directoryPath, out FromPool<List<FileSystemEntry>> entries) {
      try {
        entries = _fileSystem.GetDirectoryFiles(directoryPath);
        return true;
      } catch (Exception e) {
        OnError(directoryPath, e);
        // Assume no entries available on error, so we can continue processing safely
        entries = _entryListPool.AllocateFrom();
        return false;
      }
    }

    public void WaitForTask(Task task) {
      while (true) {
        var completed = task.Wait(TimeSpan.FromMilliseconds(50));
        if (completed)
          break;
        OnPulse();
      }
    }

    public Task<T> TraverseDirectoryAsync<T>(FileSystemEntry directoryEntry, IDirectorCollector<T> collector, bool followLinks = false) {
      return TraverseDirectoryAsync<T>(directoryEntry, collector, followLinks, 0, true);
    }

    private async Task<T> TraverseDirectoryAsync<T>(
      FileSystemEntry directoryEntry,
      IDirectorCollector<T> collector,
      bool followLinks,
      int depth,
      bool skipNotification) {

      return await Task.Run(async () => {
        if (!skipNotification) {
          OnDirectoryTraversing(directoryEntry);
        }
        var result = await TraverseDirectoryEntriesAsync(
          directoryEntry,
          collector,
          followLinks,
          depth).ConfigureAwait(false);
        if (!skipNotification) {
          OnDirectoryTraversed(directoryEntry);
        }
        return result;
      }).ConfigureAwait(false);
    }

    private async Task<T> TraverseDirectoryEntriesAsync<T>(
      FileSystemEntry directoryEntry,
      IDirectorCollector<T> collector,
      bool followLinks,
      int depth) {

      OnEntriesDiscovering(directoryEntry);
      var entries = GetDirectoryEntries(directoryEntry.Path);
      OnEntriesDiscovered(directoryEntry, entries.Item);

      // Notify collector
      var collectorItem = collector.CreateItemForDirectory(_fileSystem, directoryEntry, depth);
      var additionalTask = collector.OnDirectoryEntriesEnumerated(_fileSystem, collectorItem, directoryEntry, entries.Item);

      // Create tasks for children directories
      List<Task<T>>? childDirectoriesTasks = null;
      foreach (var entry in entries.Item) {
        if (entry.IsDirectory) {
          bool isRealDirectory = !entry.IsReparsePoint;
          bool followDirectoryLink = entry.IsReparsePoint && followLinks;
          if (isRealDirectory || followDirectoryLink) {
            childDirectoriesTasks ??= new List<Task<T>>();
            childDirectoriesTasks.Add(TraverseDirectoryAsync(entry, collector, followLinks, depth + 1, false));
          }
        }
      }

      entries.Dispose();

      if (additionalTask != null && !additionalTask.IsCompleted) {
        await additionalTask.ConfigureAwait(false);
      }

      if (childDirectoriesTasks != null && childDirectoriesTasks.Count > 0) {
        var childResults = await Task.WhenAll(childDirectoriesTasks).ConfigureAwait(false);
        foreach (var childResult in childResults) {
          collector.OnDirectoryTraversed(_fileSystem, collectorItem, childResult);
        }
      }

      return collectorItem;
    }

    public Task CopyDirectoryAsync(
      FileSystemEntry sourceDirectory,
      FullPath destinationPath,
      CopyOptions options,
      IFileComparer fileComparer,
      bool destinationDirectoryIsNew) {
      return CopyDirectoryAsync(sourceDirectory, destinationPath, null, options, fileComparer, destinationDirectoryIsNew, true);
    }

    private async Task CopyDirectoryAsync(
      FileSystemEntry sourceDirectory,
      FullPath destinationPath,
      FileSystemEntry? destinationDirectoryEntry,
      CopyOptions options,
      IFileComparer fileComparer,
      bool destinationDirectoryIsNew,
      bool skipNotification) {

      await Task.Run(async () => {
        if (!skipNotification)
          OnDirectoryTraversing(sourceDirectory);

        await CopyDirectoryEntriesAsync(
          sourceDirectory,
          destinationPath,
          destinationDirectoryEntry,
          options,
          fileComparer,
          destinationDirectoryIsNew).ConfigureAwait(false);

        if (!skipNotification)
          OnDirectoryTraversed(sourceDirectory);
      }).ConfigureAwait(false);
    }

    private async Task CopyDirectoryEntriesAsync(
      FileSystemEntry sourceDirectory,
      FullPath destinationPath,
      FileSystemEntry? destinationDirectoryEntry,
      CopyOptions options,
      IFileComparer fileComparer,
      bool destinationDirectoryIsNew) {

      FileSystemEntry destinationDirectory;
      if (destinationDirectoryEntry.HasValue) {
        destinationDirectory = destinationDirectoryEntry.Value;
      } else {
        var destinationDirectoryOpt = GetOrCreateDirectory(destinationPath, destinationDirectoryIsNew);
        if (destinationDirectoryOpt == null)
          return;
        destinationDirectory = destinationDirectoryOpt.Value;
      }

      OnEntriesDiscovering(sourceDirectory);
      var sourceReadSuccess = TryGetDirectoryEntries(sourceDirectory.Path, out var sourceEntries);
      OnEntriesDiscovered(sourceDirectory, sourceEntries.Item);

      // If source directory could not be read, do NOT proceed with deleting destination files.
      if (!sourceReadSuccess) {
        sourceEntries.Dispose();
        return;
      }

      FromPool<List<FileSystemEntry>> destinationEntries;
      FromPool<SmallSet<FileSystemEntry>> destinationSet;

      try {
        destinationEntries = destinationDirectoryIsNew
          ? _entryListPool.AllocateFrom()
          : GetDirectoryEntries(destinationPath);
        destinationSet = _entrySetPool.AllocateFrom();
        destinationSet.Item.SetList(destinationEntries.Item);
      } catch {
        sourceEntries.Dispose();
        throw;
      }

      FromPool<List<FileSystemEntry>> entriesToDelete;
      try {
        OnEntriesToDeleteDiscovering(destinationDirectory);
        entriesToDelete = ComputeDestinationEntriesToDelete(sourceEntries.Item, destinationEntries.Item, options);
        OnEntriesToDeleteDiscovered(destinationDirectory, entriesToDelete.Item);
      } catch {
        sourceEntries.Dispose();
        destinationEntries.Dispose();
        destinationSet.Dispose();
        throw;
      }

      if (entriesToDelete.Item.Count > 0) {
        using var deleteTaskList = _taskListPool.AllocateFrom();
        foreach (var entry in entriesToDelete.Item) {
          deleteTaskList.Item.Add(DeleteEntryAsync(entry));
        }
        await Task.WhenAll(deleteTaskList.Item).ConfigureAwait(false);
      }

      // 1. Process files in current directory (small files synchronously, large files in background tasks)
      using var fileTaskList = _taskListPool.AllocateFrom();
      foreach (var entry in sourceEntries.Item) {
        PerformOrScheduleFileEntryCopy(entry, destinationDirectory, fileComparer, destinationSet.Item, fileTaskList.Item, options);
      }

      // 2. Prepare subdirectories tasks without LINQ lambda allocations
      using (var subDirTaskList = _taskListPool.AllocateFrom()) {
        foreach (var sourceEntry in sourceEntries.Item) {
          if (sourceEntry.IsDirectory && !sourceEntry.IsReparsePoint) {
            var destinationEntryPath = new FullPath(destinationDirectory.Path, sourceEntry.Name);
            var destinationExists = destinationSet.Item.TryGet(sourceEntry, out var childDestEntry);
            subDirTaskList.Item.Add(CopyDirectoryAsync(
              sourceEntry,
              destinationEntryPath,
              destinationExists ? childDestEntry : (FileSystemEntry?)null,
              options,
              fileComparer,
              !destinationExists,
              false/*skipNotification*/));
          }
        }

        // 3. Recycle all pooled collections immediately before waiting
        entriesToDelete.Dispose();
        sourceEntries.Dispose();
        destinationEntries.Dispose();
        destinationSet.Dispose();

        // 4. Await both large files in the current directory and all subdirectories
        if (fileTaskList.Item.Count > 0 && subDirTaskList.Item.Count > 0) {
          await Task.WhenAll(Task.WhenAll(fileTaskList.Item), Task.WhenAll(subDirTaskList.Item)).ConfigureAwait(false);
        } else if (fileTaskList.Item.Count > 0) {
          await Task.WhenAll(fileTaskList.Item).ConfigureAwait(false);
        } else if (subDirTaskList.Item.Count > 0) {
          await Task.WhenAll(subDirTaskList.Item).ConfigureAwait(false);
        }
      }
    }

    private FileSystemEntry? GetOrCreateDirectory(FullPath destinationPath, bool destinationDirectoryIsNew) {
      var directoryCreated = false;
      // Create destination directory (throw if error)
      if (destinationDirectoryIsNew) {
        try {
          _fileSystem.CreateDirectory(destinationPath);
          directoryCreated = true;
        } catch (Exception e) {
          OnError(destinationPath, e);
        }
      }

      FileSystemEntry destinationDirectory;
      try {
        destinationDirectory = _fileSystem.GetEntry(destinationPath);
      } catch (Exception e) {
        OnError(destinationPath, e);
        // If we can't find the destination entry, give up this directory.
        return null;
      }

      if (directoryCreated)
        OnDirectoryCreated(destinationDirectory);
      return destinationDirectory;
    }

    private FromPool<List<FileSystemEntry>> ComputeDestinationEntriesToDelete(
      List<FileSystemEntry> sourceEntries,
      List<FileSystemEntry> destinationEntries,
      CopyOptions options) {

      var entriesToDelete = _entryListPool.AllocateFrom();

      if (destinationEntries.Count == 0)
        return entriesToDelete;

      // Note: DeleteExtraFiles is a strict superset of DeleteMismatchedFiles
      if ((options & CopyOptions.DeleteExtraFiles) != 0) {
        // Delete files in destination that are either not present in source, or
        // present in source but with a different kind (e.g. file vs directory).
        using var extraEntries = _entryListPool.AllocateFrom();
        using var sourceDict = _sourceDictPool.AllocateFrom();
        foreach (var src in sourceEntries) {
          sourceDict.Item.TryAdd(src.Name, src);
        }

        foreach (var dst in destinationEntries) {
          if (!sourceDict.Item.TryGetValue(dst.Name, out var src)) {
            extraEntries.Item.Add(dst);
          } else if (dst.IsFile != src.IsFile ||
                     dst.IsDirectory != src.IsDirectory ||
                     dst.IsReparsePoint != src.IsReparsePoint) {
            extraEntries.Item.Add(dst);
          }
        }

        entriesToDelete.Item.AddRange(extraEntries.Item);
      } else if ((options & CopyOptions.DeleteMismatchedFiles) != 0) {
        // Fast O(N) lookup instead of O(N*M) nested loop
        using var sourceDict = _sourceDictPool.AllocateFrom();
        foreach (var src in sourceEntries) {
          sourceDict.Item.TryAdd(src.Name, src);
        }

        foreach (var dst in destinationEntries) {
          if (sourceDict.Item.TryGetValue(dst.Name, out var src)) {
            // Same name, different "kind"?
            if (dst.IsFile != src.IsFile ||
                dst.IsDirectory != src.IsDirectory ||
                dst.IsReparsePoint != src.IsReparsePoint) {
              entriesToDelete.Item.Add(dst);
            }
          }
        }
      }
      return entriesToDelete;
    }

    private struct CopyFileData(ParallelFileSystem instance, NoAllocStopwatch stopwatch) {
      public ParallelFileSystem Instance { get; } = instance;
      public NoAllocStopwatch Stopwatch { get; } = stopwatch;
    }

    private void PerformOrScheduleFileEntryCopy(
      FileSystemEntry sourceEntry,
      FileSystemEntry destinationDirectory,
      IFileComparer fileComparer,
      SmallSet<FileSystemEntry> destinationSet,
      List<Task> fileTaskList,
      CopyOptions options) {

      if (!sourceEntry.IsFile && !sourceEntry.IsReparsePoint) {
        return;
      }

      var destinationExists = destinationSet.TryGet(sourceEntry, out var destinationEntry);
      var destinationPath = destinationExists
        ? destinationEntry.Path
        : new FullPath(destinationDirectory.Path, sourceEntry.Name);

      if (destinationExists) {
        try {
          if (fileComparer.CompareFiles(sourceEntry, destinationEntry)) {
            OnFileCopySkipped(sourceEntry);
            return;
          }
        } catch (Exception e) {
          // If we can't compare files, log error and continue with normal copy operation.
          OnError(sourceEntry.Path, e);
        }
      }

      var copyFileOptions = CopyFileOptions.Default;
      if ((options & CopyOptions.NoClone) != 0) {
        copyFileOptions |= CopyFileOptions.NoClone;
      }

      // If file size is >= threshold, offload copying to a background task
      if (sourceEntry.IsFile && sourceEntry.FileSize >= LargeFileAsyncThreshold) {
        fileTaskList.Add(Task.Run(() => PerformCopyFile(sourceEntry, destinationPath, destinationEntry, destinationExists, copyFileOptions)));
      } else {
        PerformCopyFile(sourceEntry, destinationPath, destinationEntry, destinationExists, copyFileOptions);
      }
    }

    private void PerformCopyFile(
      FileSystemEntry sourceEntry,
      FullPath destinationPath,
      FileSystemEntry destinationEntry,
      bool destinationExists,
      CopyFileOptions copyFileOptions) {
      var sw = _stopwatchFactory.Create();
      OnFileCopying(sourceEntry);
      try {
        var copyData = new CopyFileData(this, sw);
        if (destinationExists) {
          _fileSystem.CopyFile(sourceEntry, destinationEntry, copyFileOptions, copyData, _copyFileCallback);
        } else {
          _fileSystem.CopyFile(sourceEntry, destinationPath, copyFileOptions, copyData, _copyFileCallback);
        }
      } catch (Exception e) {
        OnError(sourceEntry.Path, e);
      }
      OnFileCopied(sourceEntry, sw.Elapsed, sourceEntry.FileSize);
    }

    public Task CompactDirectoryAsync(
      FileSystemEntry sourceDirectory,
      FullPath destinationPath,
      IFileComparer fileComparer,
      bool dryRun = false) {
      return Task.Run(async () => {
        OnDirectoryTraversing(sourceDirectory);
        await CompactDirectoryEntriesAsync(sourceDirectory, destinationPath, fileComparer, dryRun).ConfigureAwait(false);
        OnDirectoryTraversed(sourceDirectory);
      });
    }

    private async Task CompactDirectoryEntriesAsync(
      FileSystemEntry sourceDirectory,
      FullPath destinationPath,
      IFileComparer fileComparer,
      bool dryRun) {

      OnEntriesDiscovering(sourceDirectory);
      if (!TryGetDirectoryEntries(sourceDirectory.Path, out var sourceEntries)) {
        return;
      }
      OnEntriesDiscovered(sourceDirectory, sourceEntries.Item);

      if (!TryGetDirectoryEntries(destinationPath, out var destinationEntries)) {
        sourceEntries.Dispose();
        return;
      }

      FromPool<SmallSet<FileSystemEntry>> destinationSet = _entrySetPool.AllocateFrom();
      destinationSet.Item.SetList(destinationEntries.Item);

      using var fileTaskList = _taskListPool.AllocateFrom();
      foreach (var entry in sourceEntries.Item) {
        if (entry.IsFile && !entry.IsReparsePoint) {
          if (destinationSet.Item.TryGet(entry, out var destinationEntry) && destinationEntry.IsFile && !destinationEntry.IsReparsePoint) {
            try {
              if (fileComparer.CompareFiles(entry, destinationEntry)) {
                if (entry.FileSize >= LargeFileAsyncThreshold) {
                  fileTaskList.Item.Add(Task.Run(() => PerformCompactFile(entry, destinationEntry.Path, dryRun)));
                } else {
                  PerformCompactFile(entry, destinationEntry.Path, dryRun);
                }
              } else {
                OnFileCompactSkipped(entry);
              }
            } catch (Exception e) {
              OnError(entry.Path, e);
            }
          } else {
            OnFileCompactSkipped(entry);
          }
        }
      }

      using (var subDirTaskList = _taskListPool.AllocateFrom()) {
        foreach (var sourceEntry in sourceEntries.Item) {
          if (sourceEntry.IsDirectory && !sourceEntry.IsReparsePoint) {
            if (destinationSet.Item.TryGet(sourceEntry, out var childDestEntry) && childDestEntry.IsDirectory && !childDestEntry.IsReparsePoint) {
              subDirTaskList.Item.Add(CompactDirectoryAsync(sourceEntry, childDestEntry.Path, fileComparer, dryRun));
            }
          }
        }

        sourceEntries.Dispose();
        destinationEntries.Dispose();
        destinationSet.Dispose();

        if (fileTaskList.Item.Count > 0 && subDirTaskList.Item.Count > 0) {
          await Task.WhenAll(Task.WhenAll(fileTaskList.Item), Task.WhenAll(subDirTaskList.Item)).ConfigureAwait(false);
        } else if (fileTaskList.Item.Count > 0) {
          await Task.WhenAll(fileTaskList.Item).ConfigureAwait(false);
        } else if (subDirTaskList.Item.Count > 0) {
          await Task.WhenAll(subDirTaskList.Item).ConfigureAwait(false);
        }
      }
    }

    private void PerformCompactFile(FileSystemEntry sourceEntry, FullPath destinationPath, bool dryRun) {
      var sw = _stopwatchFactory.Create();
      OnFileCompacting(sourceEntry);
      if (!dryRun) {
        try {
          _fileSystem.CloneFile(sourceEntry, destinationPath);
        } catch (Exception e) {
          OnError(sourceEntry.Path, e);
        }
      }
      OnFileCompacted(sourceEntry, sw.Elapsed, sourceEntry.FileSize);
    }

    /// <summary>
    /// Delete a file system entry. Recurse through directories if
    /// the entry is a directory.
    /// </summary>
    public Task DeleteEntryAsync(FileSystemEntry entry) {
      return DeleteEntryAsync(entry, static _ => true);
    }

    /// <summary>
    /// Delete a file system entry. Recurse through directories if
    /// the entry is a directory.
    /// </summary>
    public Task DeleteEntryAsync(FileSystemEntry entry, Func<FileSystemEntry, bool> includeFilter) {
      if (entry.IsFile || entry.IsReparsePoint)
        return Task.Run(() => DeleteSingleEntry(entry, includeFilter));
      return DeleteDirectoryAsync(entry, includeFilter);
    }

    private async Task DeleteDirectoryAsync(FileSystemEntry directoryEntry, Func<FileSystemEntry, bool> includeFilter) {
      await DeleteDirectoryEntriesAsync(directoryEntry, includeFilter).ConfigureAwait(false);
      DeleteSingleEntry(directoryEntry, includeFilter);
    }

    private async Task DeleteDirectoryEntriesAsync(FileSystemEntry directoryEntry, Func<FileSystemEntry, bool> includeFilter) {
      OnEntriesToDeleteDiscovering(directoryEntry);
      if (!TryGetDirectoryEntries(directoryEntry.Path, out var entries)) {
        return;
      }

      OnEntriesToDeleteDiscovered(directoryEntry, entries.Item);
      using (var deleteSubDirTaskList = _taskListPool.AllocateFrom()) {
        foreach (var entry in entries.Item) {
          if (entry.IsDirectory && !entry.IsReparsePoint) {
            deleteSubDirTaskList.Item.Add(DeleteDirectoryEntriesAsync(entry, includeFilter));
          }
        }
        if (deleteSubDirTaskList.Item.Count > 0) {
          await Task.WhenAll(deleteSubDirTaskList.Item).ConfigureAwait(false);
        }
      }

      DeleteEntries(entries.Item, includeFilter);
      OnEntriesToDeleteProcessed(directoryEntry, entries.Item);
      entries.Dispose();
    }

    private void DeleteEntries(List<FileSystemEntry> entries, Func<FileSystemEntry, bool> includeFilter) {
      // Delete all entries
      foreach (var entry in entries) {
        DeleteSingleEntry(entry, includeFilter);
      }
    }

    private void DeleteSingleEntry(FileSystemEntry entry, Func<FileSystemEntry, bool> includeFilter) {
      if (!includeFilter(entry))
        return;

      var sw = _stopwatchFactory.Create();
      OnEntryDeleting(entry);
      try {
        _fileSystem.DeleteEntry(entry);
      } catch (Exception e) {
        OnError(entry.Path, e);
      }
      OnEntryDeleted(entry, sw.Elapsed);
    }

    protected virtual void OnError(FullPath path, Exception obj) {
      var handler = Error;
      if (handler != null) handler(path, obj);
    }

    protected virtual void OnPulse() {
      var handler = Pulse;
      if (handler != null) handler();
    }

    protected virtual void OnEntriesDiscovered(FileSystemEntry arg1, List<FileSystemEntry> arg2) {
      var handler = EntriesDiscovered;
      if (handler != null) handler(arg1, arg2);
    }

    protected virtual void OnEntriesToDeleteDiscovering(FileSystemEntry obj) {
      var handler = EntriesToDeleteDiscovering;
      if (handler != null) handler(obj);
    }

    protected virtual void OnEntriesToDeleteDiscovered(FileSystemEntry directoryEntry, List<FileSystemEntry> obj) {
      var handler = EntriesToDeleteDiscovered;
      if (handler != null) handler(directoryEntry, obj);
    }

    protected virtual void OnEntryDeleting(FileSystemEntry arg1) {
      var handler = EntryDeleting;
      if (handler != null) handler(arg1);
    }

    protected virtual void OnEntryDeleted(FileSystemEntry arg1, TimeSpan arg2) {
      var handler = EntryDeleted;
      if (handler != null) handler(arg1, arg2);
    }

    protected virtual void OnFileCopySkipped(FileSystemEntry obj) {
      var handler = FileCopySkipped;
      if (handler != null) handler(obj);
    }

    protected virtual void OnFileCopying(FileSystemEntry arg1) {
      var handler = FileCopying;
      if (handler != null) handler(arg1);
    }

    protected virtual void OnFileCopyingProgress(FileSystemEntry arg1, TimeSpan arg2, long arg3) {
      var handler = FileCopyingProgress;
      if (handler != null) handler(arg1, arg2, arg3);
    }

    protected virtual void OnFileCopied(FileSystemEntry arg1, TimeSpan arg2, long arg3) {
      var handler = FileCopied;
      if (handler != null) handler(arg1, arg2, arg3);
    }

    protected virtual void OnFileCompacting(FileSystemEntry arg1) {
      var handler = FileCompacting;
      if (handler != null) handler(arg1);
    }

    protected virtual void OnFileCompacted(FileSystemEntry arg1, TimeSpan arg2, long arg3) {
      var handler = FileCompacted;
      if (handler != null) handler(arg1, arg2, arg3);
    }

    protected virtual void OnFileCompactSkipped(FileSystemEntry obj) {
      var handler = FileCompactSkipped;
      if (handler != null) handler(obj);
    }

    protected virtual void OnDirectoryTraversing(FileSystemEntry obj) {
      var handler = DirectoryTraversing;
      if (handler != null) handler(obj);
    }

    protected virtual void OnDirectoryTraversed(FileSystemEntry obj) {
      var handler = DirectoryTraversed;
      if (handler != null) handler(obj);
    }

    protected virtual void OnDirectoryCreated(FileSystemEntry obj) {
      var handler = DirectoryCreated;
      if (handler != null) handler(obj);
    }

    protected virtual void OnEntriesToDeleteProcessed(FileSystemEntry arg1, List<FileSystemEntry> arg2) {
      var handler = EntriesToDeleteProcessed;
      if (handler != null) handler(arg1, arg2);
    }

    protected virtual void OnEntriesDiscovering(FileSystemEntry obj) {
      var handler = EntriesDiscovering;
      if (handler != null) handler(obj);
    }
  }
}