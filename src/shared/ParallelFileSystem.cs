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
using System.Collections.Generic;
using System.Threading.Tasks;
using mtsuite.shared.Collections;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtsuite.shared;

public sealed class ParallelFileSystem : IParallelFileSystem {
  private readonly IFileSystem _fileSystem;
  private readonly long _largeFileAsyncThreshold;
  private readonly INoAllocStopwatchFactory _stopwatchFactory;
  private readonly IPool<List<FileSystemEntry>> _entryListPool;
  private readonly IPool<List<Task>> _taskListPool;
  private readonly IPool<SmallSet<FileSystemEntry>> _entrySetPool;
  private readonly IPool<Dictionary<string, FileSystemEntry>> _sourceDictPool;

  /// <summary>
  /// Callback to <see cref="IFileSystem.CopyFile"/>, stored in a field to avoid GC allocation
  /// at every invocation.
  /// </summary>
  private readonly CopyFileCallback<CopyFileData> _copyFileCallback = static (ref FileSystemEntry sourceEntry, long bytesFromPreviousCall, long bytesSoFar, long totalBytes, ref CopyFileData data) => {
    data.Instance.OnFileCopyingProgress(sourceEntry, data.Stopwatch.Elapsed, bytesFromPreviousCall, bytesSoFar);
  };

  /// <summary>
  /// Callback to <see cref="IFileComparer.CompareFiles"/>, stored in a field to avoid GC allocation
  /// at every invocation.
  /// </summary>
  private readonly CompareFileCallback<CompareFileData> _compareFileCallback = static (ref FileSystemEntry sourceEntry, long bytesFromPreviousCall, long bytesSoFar, long totalBytes, ref CompareFileData data) => {
    data.Instance.OnFileComparingProgress(sourceEntry, data.Stopwatch.Elapsed, bytesFromPreviousCall, bytesSoFar);
  };

  public ParallelFileSystem(
    IFileSystem fileSystem,
    MtPoolFactory poolFactory,
    INoAllocStopwatchFactory? stopwatchFactory = null,
    long largeFileAsyncThreshold = DefaultLargeFileAsyncThreshold) {
    ArgumentNullException.ThrowIfNull(fileSystem);
    ArgumentNullException.ThrowIfNull(poolFactory);
    
    _fileSystem = fileSystem;
    _largeFileAsyncThreshold = largeFileAsyncThreshold;
    _stopwatchFactory = stopwatchFactory ?? NoAllocStopwatchFactory.Instance;

    _entryListPool = poolFactory.CreateList<FileSystemEntry>("ParallelFileSystem.EntryList");
    _taskListPool = poolFactory.CreateList<Task>("ParallelFileSystem.TaskList");

    _entrySetPool = poolFactory.Create(
      "ParallelFileSystem.EntrySet",
      static () => new SmallSet<FileSystemEntry>(FileSystemEntryNameComparer.Instance),
      static x => x.Clear());

    _sourceDictPool = poolFactory.Create(
      "ParallelFileSystem.SourceDict",
      static () => new Dictionary<string, FileSystemEntry>(256, PathHelpers.FileNameComparer),
      static d => d.Clear());
  }

  /// <summary>
  /// Default file size threshold (10 MB) above which file copying is offloaded to a background task.
  /// </summary>
  private const long DefaultLargeFileAsyncThreshold = 10 * 1024 * 1024;

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
  public event Action<FileSystemEntry>? FileComparing;
  public event Action<FileSystemEntry, TimeSpan, long, long>? FileComparingProgress;
  public event Action<FileSystemEntry, TimeSpan, long>? FileCompared;
  public event Action<FileSystemEntry>? FileCopying;
  public event Action<FileSystemEntry, TimeSpan, long, long>? FileCopyingProgress;
  public event Action<FileSystemEntry, TimeSpan, long>? FileCopied;
  public event Action<FileSystemEntry>? FileCloning;
  public event Action<FileSystemEntry, TimeSpan, long>? FileCloned;
  public event Action<FileSystemEntry>? FileCloneSkipped;
  public event Action<FileSystemEntry>? FileAlreadyCloned;
  public event Action<FileSystemEntry>? DirectoryTraversing;
  public event Action<FileSystemEntry>? DirectoryTraversed;
  public event Action<FileSystemEntry>? DirectoryCreated;

  public void WaitForTask(Task task) {
    while (true) {
      var completed = task.Wait(TimeSpan.FromMilliseconds(50));
      if (completed)
        break;
      OnPulse();
    }
  }

  public Task<T> TraverseDirectoryAsync<T>(FileSystemEntry directoryEntry, IDirectorCollector<T> collector,
    bool followLinks = false) {
    ArgumentNullException.ThrowIfNull(collector);
    return TraverseDirectoryAsync(directoryEntry, collector, followLinks, 0, true);
  }

  public Task CopyDirectoryAsync(FileSystemEntry sourceDirectory, FullPath destinationPath, CopyOptions options,
    IFileComparer fileComparer) {
    ArgumentNullException.ThrowIfNull(fileComparer);

    // Lookup destination directory
    FileSystemEntry? destinationDirectory;
    try {
      destinationDirectory = _fileSystem.GetEntry(destinationPath);
    }
    catch {
      destinationDirectory = null;
    }

    return CopyDirectoryAsync(sourceDirectory, destinationPath, destinationDirectory, options, fileComparer, null,
      true);
  }

  public Task CompactDirectoryAsync(FileSystemEntry sourceDirectory, FileSystemEntry destinationDirectory, IFileComparer fileComparer, bool dryRun) {
    ArgumentNullException.ThrowIfNull(fileComparer);

    // CompactDirectoryEntriesAsync does a lot of synchronous I/O, so we run it in a dedicated task/thread.
    return Task.Run(async () => {
      OnDirectoryTraversing(sourceDirectory);
      await CompactDirectoryEntriesAsync(sourceDirectory, destinationDirectory, fileComparer, dryRun).ConfigureAwait(false);
      OnDirectoryTraversed(sourceDirectory);
    });
  }
  
  private async Task<T> TraverseDirectoryAsync<T>(
    FileSystemEntry directoryEntry,
    IDirectorCollector<T> collector,
    bool followLinks,
    int depth,
    bool skipNotification) {

    // TraverseDirectoryEntriesAsync does a lot of synchronous I/O, so we run it in a dedicated task/thread.
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

  private async Task CopyDirectoryAsync(
    FileSystemEntry sourceDirectory,
    FullPath destinationPath,
    FileSystemEntry? destinationDirectoryEntry,
    CopyOptions options,
    IFileComparer fileComparer,
    bool? useCloning,
    bool skipNotification) {

    // CopyDirectoryEntriesAsync does a lot of synchronous I/O, so we run it in a dedicated task/thread.
    await Task.Run(async () => {
      if (!skipNotification)
        OnDirectoryTraversing(sourceDirectory);

      await CopyDirectoryEntriesAsync(
        sourceDirectory,
        destinationPath,
        destinationDirectoryEntry,
        options,
        fileComparer,
        useCloning);

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
    bool? useCloning) {
    var destinationDirectory = destinationDirectoryEntry ?? CreateDirectory(destinationPath);
    bool isCloningActive = useCloning ?? ((options & CopyOptions.NoClone) == 0 &&
                                          _fileSystem.Extension.IsCloningSupported(sourceDirectory.Path,
                                            destinationDirectory.Path));

    Task finalTask;
    {
      OnEntriesDiscovering(sourceDirectory);
      using var sourceEntries = _fileSystem.GetDirectoryFiles(sourceDirectory.Path);
      OnEntriesDiscovered(sourceDirectory, sourceEntries.Item);

      using var destinationEntries = destinationDirectoryEntry.HasValue
        ? _fileSystem.GetDirectoryFiles(destinationPath)
        : _entryListPool.AllocateFrom();

      using var destinationSet = _entrySetPool.AllocateFrom();
      destinationSet.Item.SetList(destinationEntries.Item);

      // 1. Compute and process deletion of extra files in destination
      {
        OnEntriesToDeleteDiscovering(destinationDirectory);
        using var entriesToDelete =
          ComputeDestinationEntriesToDelete(sourceEntries.Item, destinationEntries.Item, options);
        OnEntriesToDeleteDiscovered(destinationDirectory, entriesToDelete.Item);

        if (entriesToDelete.Item.Count > 0) {
          using var deleteTaskList = _taskListPool.AllocateFrom();
          foreach (var entry in entriesToDelete.Item) {
            deleteTaskList.Item.Add(DeleteEntryAsync(entry));
          }

          await Task.WhenAll(deleteTaskList.Item).ConfigureAwait(false);
        }
      }

      // 2. Copy source files/reparse points, subdirectories
      {
        using var taskList = _taskListPool.AllocateFrom();
        foreach (var entry in sourceEntries.Item) {
          if (entry.IsFile || entry.IsReparsePoint) {
            PerformOrScheduleFileEntryCopy(entry, destinationDirectory, fileComparer, destinationSet.Item,
              taskList.Item, isCloningActive);
          }
          else if (entry.IsDirectory) {
            var destinationEntryPath = new FullPath(destinationDirectory.Path, entry.Name);
            var destinationExists = destinationSet.Item.TryGet(entry, out var childDestEntry);
            taskList.Item.Add(CopyDirectoryAsync(
              entry,
              destinationEntryPath,
              destinationExists ? childDestEntry : null,
              options,
              fileComparer,
              isCloningActive,
              false /*skipNotification*/));
          }
        }

        // The final task is to wait for all intermediate "copy" sub-tasks
        finalTask = Task.WhenAll(taskList.Item);
      }
    }

    // 4. Await both large files in the current directory and all subdirectories
    await finalTask.ConfigureAwait(false);
  }

  private FileSystemEntry CreateDirectory(FullPath destinationPath) {
    // Create destination directory (throw if error)
    _fileSystem.CreateDirectory(destinationPath);
    var destinationDirectory = _fileSystem.GetEntry(destinationPath);
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

  private void PerformOrScheduleFileEntryCopy(
    FileSystemEntry sourceEntry,
    FileSystemEntry destinationDirectory,
    IFileComparer fileComparer,
    SmallSet<FileSystemEntry> destinationDirectoryEntries,
    List<Task> fileTaskList,
    bool useCloning) {

    // This method only copies regular files and reparse points
    if (!sourceEntry.IsFile && !sourceEntry.IsReparsePoint) {
      return;
    }

    var destinationExists = destinationDirectoryEntries.TryGet(sourceEntry, out var destinationEntry);
    var destinationPath = destinationExists
      ? destinationEntry.Path
      : new FullPath(destinationDirectory.Path, sourceEntry.Name);

    if (destinationExists) {
      try {
        var areEqual = CompareFiles(fileComparer, sourceEntry, destinationEntry);
        if (areEqual) {
          OnFileCopySkipped(sourceEntry);
          return;
        }
      } catch (Exception e) {
        // If we can't compare files, log error and continue with normal copy operation.
        OnError(sourceEntry.Path, e);
      }
    }

    if (useCloning && sourceEntry.IsFile && !sourceEntry.IsReparsePoint) {
      // If file size is >= threshold, offload cloning to a background task
      if (sourceEntry.FileSize >= _largeFileAsyncThreshold) {
        fileTaskList.Add(Task.Run(() => PerformCloneFile(sourceEntry, destinationPath)));
      } else {
        PerformCloneFile(sourceEntry, destinationPath);
      }
    } else {
      var copyFileOptions = CopyFileOptions.Default | CopyFileOptions.NoClone;
      // If file size is >= threshold, offload copying to a background task
      if (sourceEntry.IsFile && sourceEntry.FileSize >= _largeFileAsyncThreshold) {
        fileTaskList.Add(Task.Run(() => PerformCopyFile(sourceEntry, destinationPath, destinationEntry, destinationExists, copyFileOptions)));
      } else {
        PerformCopyFile(sourceEntry, destinationPath, destinationEntry, destinationExists, copyFileOptions);
      }
    }
  }

  private void PerformCloneFile(
    FileSystemEntry sourceEntry,
    FullPath destinationPath) {
    var sw = _stopwatchFactory.Create();
    OnFileCloning(sourceEntry);
    try {
      _fileSystem.Extension.CloneFile(sourceEntry, destinationPath);
    } catch (Exception e) {
      OnError(sourceEntry.Path, e);
    }
    OnFileCloned(sourceEntry, sw.Elapsed, sourceEntry.FileSize);
  }

  private void PerformCopyFile(
    FileSystemEntry sourceEntry,
    FullPath destinationPath,
    FileSystemEntry? destinationEntry,
    bool destinationExists,
    CopyFileOptions copyFileOptions) {
    var sw = _stopwatchFactory.Create();
    OnFileCopying(sourceEntry);
    try {
      var copyData = new CopyFileData(this, sw);
      if (destinationExists && destinationEntry.HasValue) {
        _fileSystem.CopyFile(sourceEntry, destinationEntry.Value, copyFileOptions, copyData, _copyFileCallback);
      } else {
        _fileSystem.CopyFile(sourceEntry, destinationPath, copyFileOptions, copyData, _copyFileCallback);
      }
    } catch (Exception e) {
      OnError(sourceEntry.Path, e);
    }
    OnFileCopied(sourceEntry, sw.Elapsed, sourceEntry.FileSize);
  }
  
  private async Task CompactDirectoryEntriesAsync(
    FileSystemEntry sourceDirectory,
    FileSystemEntry destinationDirectory,
    IFileComparer fileComparer,
    bool dryRun) {

    Task? additionalTask = null;
    {
      OnEntriesDiscovering(sourceDirectory);
      using var sourceEntries = _fileSystem.GetDirectoryFiles(sourceDirectory.Path);
      OnEntriesDiscovered(sourceDirectory, sourceEntries.Item);

      using var destinationEntries = _fileSystem.GetDirectoryFiles(destinationDirectory.Path);
      using var destinationSet = _entrySetPool.AllocateFrom();
      destinationSet.Item.SetList(destinationEntries.Item);

      // Process files and schedule subdirectories in current directory
      using var taskList = _taskListPool.AllocateFrom();
      foreach (var sourceEntry in sourceEntries.Item) {
        if (destinationSet.Item.TryGet(sourceEntry, out var destinationEntry)) {
          if (sourceEntry.IsRegularFile && destinationEntry.IsRegularFile) {
            PerformOrScheduleFileEntryClone(sourceEntry, destinationEntry, fileComparer, dryRun, taskList.Item);
          }
          else if (sourceEntry.IsRegularDirectory && destinationEntry.IsRegularDirectory) {
            taskList.Item.Add(CompactDirectoryAsync(sourceEntry, destinationEntry, fileComparer, dryRun));
          }
        }
      }

      if (taskList.Item.Count > 0) {
        additionalTask = Task.WhenAll(taskList.Item);
      }
    }
    if (additionalTask != null) {
      await additionalTask.ConfigureAwait(false);
    }
  }

  private void PerformOrScheduleFileEntryClone(
    FileSystemEntry sourceEntry,
    FileSystemEntry destinationEntry,
    IFileComparer fileComparer,
    bool dryRun,
    List<Task> fileTaskList) {

    // If file size is >= threshold, offload copying to a background task
    if (sourceEntry.FileSize >= _largeFileAsyncThreshold) {
      fileTaskList.Add(Task.Run(() => PerformFileCloneIfNeeded(sourceEntry, destinationEntry, fileComparer, dryRun)));
    }
    else {
      PerformFileCloneIfNeeded(sourceEntry, destinationEntry, fileComparer, dryRun);
    }
  }

  private void PerformFileCloneIfNeeded(FileSystemEntry sourceEntry, FileSystemEntry destinationEntry, IFileComparer fileComparer, bool dryRun) {
    try {
      bool shouldClone;
      // 1. Decide if we should clone
      var sw = _stopwatchFactory.Create();
      OnFileComparing(sourceEntry);
      if (_fileSystem.Extension.AreFilesCloned(sourceEntry, destinationEntry)) {
        OnFileAlreadyCloned(sourceEntry);
        shouldClone = false;
      }
      else {
        var compareData = new CompareFileData(this, sw);
        var areEqual = fileComparer.CompareFiles(sourceEntry, destinationEntry, compareData, _compareFileCallback);
        if (areEqual) {
          shouldClone = true;
        }
        else {
          OnFileCloneSkipped(sourceEntry);
          shouldClone = false;
        }
      }
      OnFileCompared(sourceEntry, sw.Elapsed, sourceEntry.FileSize);

      // 2. Perform clone
      if (shouldClone) {
        PerformFileClone(sourceEntry, destinationEntry.Path, dryRun);
      }
    }
    catch (Exception e) {
      OnError(sourceEntry.Path, e);
    }
  }

  private void PerformFileClone(FileSystemEntry sourceEntry, FullPath destinationPath, bool dryRun) {
    var sw = _stopwatchFactory.Create();
    OnFileCloning(sourceEntry);
    if (!dryRun) {
      try {
        _fileSystem.Extension.CloneFile(sourceEntry, destinationPath);
      } catch (Exception e) {
        OnError(sourceEntry.Path, e);
      }
    }
    OnFileCloned(sourceEntry, sw.Elapsed, sourceEntry.FileSize);
  }

  private bool CompareFiles(IFileComparer fileComparer, FileSystemEntry entry, FileSystemEntry destinationEntry) {
    bool areEqual;
    if (fileComparer.IsFast) {
      areEqual = fileComparer.CompareFiles(entry, destinationEntry);
    } else {
      var sw = _stopwatchFactory.Create();
      OnFileComparing(entry);
      var compareData = new CompareFileData(this, sw);
      areEqual = fileComparer.CompareFiles(entry, destinationEntry, compareData, _compareFileCallback);
      OnFileCompared(entry, sw.Elapsed, entry.FileSize);
    }

    return areEqual;
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

  private void OnError(FullPath path, Exception exception) {
    var handler = Error;
    if (handler != null) handler(path, exception);
  }

  private void OnPulse() {
    var handler = Pulse;
    if (handler != null) handler();
  }

  private void OnEntriesDiscovering(FileSystemEntry directoryEntry) {
    var handler = EntriesDiscovering;
    if (handler != null) handler(directoryEntry);
  }

  private void OnEntriesDiscovered(FileSystemEntry directoryEntry, List<FileSystemEntry> entries) {
    var handler = EntriesDiscovered;
    if (handler != null) handler(directoryEntry, entries);
  }

  private void OnEntriesToDeleteDiscovering(FileSystemEntry directoryEntry) {
    var handler = EntriesToDeleteDiscovering;
    if (handler != null) handler(directoryEntry);
  }

  private void OnEntriesToDeleteDiscovered(FileSystemEntry directoryEntry, List<FileSystemEntry> entries) {
    var handler = EntriesToDeleteDiscovered;
    if (handler != null) handler(directoryEntry, entries);
  }

  private void OnEntriesToDeleteProcessed(FileSystemEntry directoryEntry, List<FileSystemEntry> entries) {
    var handler = EntriesToDeleteProcessed;
    if (handler != null) handler(directoryEntry, entries);
  }

  private void OnEntryDeleting(FileSystemEntry entry) {
    var handler = EntryDeleting;
    if (handler != null) handler(entry);
  }

  private void OnEntryDeleted(FileSystemEntry entry, TimeSpan elapsed) {
    var handler = EntryDeleted;
    if (handler != null) handler(entry, elapsed);
  }

  private void OnFileCopySkipped(FileSystemEntry sourceEntry) {
    var handler = FileCopySkipped;
    if (handler != null) handler(sourceEntry);
  }

  private void OnFileComparing(FileSystemEntry sourceEntry) {
    var handler = FileComparing;
    if (handler != null) handler(sourceEntry);
  }

  private void OnFileComparingProgress(FileSystemEntry entry, TimeSpan elapsed, long bytesFromPreviousCall, long bytesSoFar) {
    var handler = FileComparingProgress;
    if (handler != null) handler(entry, elapsed, bytesFromPreviousCall, bytesSoFar);
  }

  private void OnFileCompared(FileSystemEntry sourceEntry, TimeSpan elapsed, long totalBytes) {
    var handler = FileCompared;
    if (handler != null) handler(sourceEntry, elapsed, totalBytes);
  }

  private void OnFileCopying(FileSystemEntry sourceEntry) {
    var handler = FileCopying;
    if (handler != null) handler(sourceEntry);
  }

  private void OnFileCopyingProgress(FileSystemEntry entry, TimeSpan elapsed, long bytesFromPreviousCall, long bytesSoFar) {
    var handler = FileCopyingProgress;
    if (handler != null) handler(entry, elapsed, bytesFromPreviousCall, bytesSoFar);
  }

  private void OnFileCopied(FileSystemEntry sourceEntry, TimeSpan elapsed, long totalBytes) {
    var handler = FileCopied;
    if (handler != null) handler(sourceEntry, elapsed, totalBytes);
  }

  private void OnFileCloning(FileSystemEntry sourceEntry) {
    var handler = FileCloning;
    if (handler != null) handler(sourceEntry);
  }

  private void OnFileCloned(FileSystemEntry sourceEntry, TimeSpan elapsed, long totalBytes) {
    var handler = FileCloned;
    if (handler != null) handler(sourceEntry, elapsed, totalBytes);
  }

  private void OnFileCloneSkipped(FileSystemEntry sourceEntry) {
    var handler = FileCloneSkipped;
    if (handler != null) handler(sourceEntry);
  }

  private void OnFileAlreadyCloned(FileSystemEntry sourceEntry) {
    var handler = FileAlreadyCloned;
    if (handler != null) handler(sourceEntry);
  }

  private void OnDirectoryTraversing(FileSystemEntry directoryEntry) {
    var handler = DirectoryTraversing;
    if (handler != null) handler(directoryEntry);
  }

  private void OnDirectoryTraversed(FileSystemEntry directoryEntry) {
    var handler = DirectoryTraversed;
    if (handler != null) handler(directoryEntry);
  }

  private void OnDirectoryCreated(FileSystemEntry directoryEntry) {
    var handler = DirectoryCreated;
    if (handler != null) handler(directoryEntry);
  }
    
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

  private readonly struct CopyFileData(ParallelFileSystem instance, NoAllocStopwatch stopwatch) {
    public ParallelFileSystem Instance { get; } = instance;
    public NoAllocStopwatch Stopwatch { get; } = stopwatch;
  }

  private readonly struct CompareFileData(ParallelFileSystem instance, NoAllocStopwatch stopwatch) {
    public ParallelFileSystem Instance { get; } = instance;
    public NoAllocStopwatch Stopwatch { get; } = stopwatch;
  }
}