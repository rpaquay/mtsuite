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
using System.Linq;
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
  private readonly IPool<List<Task<bool>>> _boolTaskListPool;
  private readonly IPool<SmallSet<FileSystemEntry>> _entrySetPool;
  private readonly IPool<Dictionary<string, FileSystemEntry>> _sourceDictPool;

  /// <summary>
  /// Callback to <see cref="IFileSystem.CopyFile"/>, stored in a field to avoid GC allocation
  /// at every invocation.
  /// </summary>
  private readonly CopyFileCallback<CopyFileData> _copyFileCallback =
    static (ref FileSystemEntry sourceEntry, long bytesFromPreviousCall, long bytesSoFar, long _, ref CopyFileData data) => {
      data.Instance.OnFileCopyingProgress(sourceEntry, data.Stopwatch.Elapsed, bytesFromPreviousCall, bytesSoFar);
    };

  /// <summary>
  /// Callback to <see cref="IFileComparer.CompareFiles"/>, stored in a field to avoid GC allocation
  /// at every invocation.
  /// </summary>
  private readonly CompareFileCallback<CompareFileData> _compareFileCallback =
    static (ref FileSystemEntry sourceEntry, long bytesFromPreviousCall, long bytesSoFar, long _, ref CompareFileData data) => {
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
    _boolTaskListPool = poolFactory.CreateList<Task<bool>>("ParallelFileSystem.BoolTaskList");

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

  public Task CopyDirectoryAsync(FileSystemEntry sourceDirectory, FileSystemEntry destinationDirectory,
    CopyOptions options, IFileComparer fileComparer) {
    ArgumentNullException.ThrowIfNull(fileComparer);

    return CopyDirectoryAsync(sourceDirectory, destinationDirectory.Path, destinationDirectory, options, fileComparer,
      useCloning: null, skipNotification: true);
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
  
  public Task<bool> DeleteEntryAsync(FileSystemEntry entry, Func<FileSystemEntry, bool> includeFilter) {
    if (entry.IsFile || entry.IsReparsePoint) {
      return Task.Run(() => DeleteSingleEntry(entry, includeFilter));
    }

    if (entry.IsDirectory) {
      return DeleteDirectoryAsync(entry, includeFilter);
    }
    
    return Task.FromResult(false);
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

    var collectorItem = collector.CreateItemForDirectory(_fileSystem, directoryEntry, depth);
    
    OnEntriesDiscovering(directoryEntry);
    using var optionalEntries = GetDirectoryEntries(directoryEntry);
    if (optionalEntries == null) {
      return collectorItem;
    }
    var entries = optionalEntries.Value;
    OnEntriesDiscovered(directoryEntry, entries.Item);

    // Notify collector
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
    
    //
    // Create destination directory (if needed)
    //
    var optionalDestinationDirectory = destinationDirectoryEntry ?? CreateDirectory(destinationPath);
    if (optionalDestinationDirectory == null) {
      // Bail out if error creating destination directory
      return;
    }
    var destinationDirectory = optionalDestinationDirectory.Value;
    var isCloningActive = ShouldUseCloneFile(sourceDirectory, destinationDirectory, options, useCloning);

    Task finalTask;
    {
      //
      // Enumerate entries in source directory
      //
      OnEntriesDiscovering(sourceDirectory);
      using var optionalSourceEntries = GetDirectoryEntries(sourceDirectory);
      if (optionalSourceEntries == null) {
        // Bail out if error enumerating files in source directory
        return;
      }
      var sourceEntries = optionalSourceEntries.Value;
      OnEntriesDiscovered(sourceDirectory, sourceEntries.Item);

      //
      // Enumerate entries in destination directory
      //
      using var optionalDestinationEntries = destinationDirectoryEntry.HasValue
        ? GetDirectoryEntries(destinationDirectory)
        : _entryListPool.AllocateFrom();
      if (optionalDestinationEntries == null) {
        // Bail out if error enumerating files in destination directory
        return;
      }
      var destinationEntries = optionalDestinationEntries.Value;

      using var destinationSet = _entrySetPool.AllocateFrom();
      destinationSet.Item.SetList(destinationEntries.Item);

      //
      // 1. Compute and process deletion of extra files in destination
      //
      {
        OnEntriesToDeleteDiscovering(destinationDirectory);
        using var entriesToDelete =
          ComputeDestinationEntriesToDelete(sourceEntries.Item, destinationEntries.Item, options);
        OnEntriesToDeleteDiscovered(destinationDirectory, entriesToDelete.Item);

        if (entriesToDelete.Item.Count > 0) {
          using var deleteTaskList = _taskListPool.AllocateFrom();
          foreach (var entry in entriesToDelete.Item) {
            deleteTaskList.Item.Add(DeleteEntryAsync(entry, static _ => true));
          }

          await Task.WhenAll(deleteTaskList.Item).ConfigureAwait(false);
        }
      }

      //
      // 2. Copy source files/reparse points, subdirectories
      //
      {
        using var taskList = _taskListPool.AllocateFrom();
        foreach (var sourceEntry in sourceEntries.Item) {
          if (sourceEntry.IsRegularFile || sourceEntry.IsReparsePoint) {
            PerformOrScheduleFileEntryCopy(sourceEntry, destinationDirectory, fileComparer, destinationSet.Item,
              taskList.Item, isCloningActive);
          }
          else if (sourceEntry.IsRegularDirectory) {
            var destinationEntryPath = new FullPath(destinationDirectory.Path, sourceEntry.Name);
            var destinationExists = destinationSet.Item.TryGet(sourceEntry, out var childDestEntry);
            taskList.Item.Add(CopyDirectoryAsync(
              sourceEntry,
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

    //
    // 4. Await all pending copy operations
    //
    await finalTask.ConfigureAwait(false);
  }
  
  private async Task CompactDirectoryEntriesAsync(
    FileSystemEntry sourceDirectory,
    FileSystemEntry destinationDirectory,
    IFileComparer fileComparer,
    bool dryRun) {

    Task? additionalTask = null;
    {
      //
      // Enumerate entries in source directory
      //
      OnEntriesDiscovering(sourceDirectory);
      using var optionalSourceEntries = GetDirectoryEntries(sourceDirectory);
      if (optionalSourceEntries == null) {
        // Bail out if error enumerating files in source directory
        return;
      }
      var sourceEntries = optionalSourceEntries.Value;
      OnEntriesDiscovered(sourceDirectory, sourceEntries.Item);

      //
      // Enumerate entries in destination directory
      //
      using var optionalDestinationEntries = GetDirectoryEntries(destinationDirectory);
      if (optionalDestinationEntries == null) {
        // Bail out if error enumerating files in destination directory
        return;
      }
      var destinationEntries = optionalDestinationEntries.Value;
      using var destinationSet = _entrySetPool.AllocateFrom();
      destinationSet.Item.SetList(destinationEntries.Item);

      //
      // Process files and schedule subdirectories in current directory
      //
      using var taskList = _taskListPool.AllocateFrom();
      foreach (var sourceEntry in sourceEntries.Item) {
        if (destinationSet.Item.TryGet(sourceEntry, out var destinationEntry)) {
          if (sourceEntry.IsRegularFile && destinationEntry.IsRegularFile) {
            PerformOrScheduleCloneFileIfNeeded(sourceEntry, destinationEntry, fileComparer, dryRun, taskList.Item);
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

  /// <summary>
  /// Delete all entries of <paramref name="sourceDirectory"/> recursively, including <paramref name="sourceDirectory"/> iself
  /// </summary>
  private async Task<bool> DeleteDirectoryAsync(FileSystemEntry sourceDirectory, Func<FileSystemEntry, bool> includeFilter) {
    // DeleteDirectoryEntriesAsync does a lot of synchronous I/O, so we run it in a dedicated task/thread.
    return await Task.Run(async () => {
      var allEntriesDeleted = await DeleteDirectoryEntriesAsync(sourceDirectory, includeFilter);
      if (allEntriesDeleted) {
        allEntriesDeleted = DeleteSingleEntry(sourceDirectory, includeFilter);
      }
      return allEntriesDeleted;
    }).ConfigureAwait(false);
  }

  /// <summary>
  /// Delete all entries of <paramref name="sourceDirectory"/> recursively, but not <paramref name="sourceDirectory"/> iself
  /// </summary>
  private async Task<bool> DeleteDirectoryEntriesAsync(FileSystemEntry sourceDirectory, Func<FileSystemEntry, bool> includeFilter) {
    //
    // Enumerate entries in source directory
    //
    OnEntriesToDeleteDiscovering(sourceDirectory);
    //OnEntriesDiscovering(sourceDirectory);
    using var optionalSourceEntries = GetDirectoryEntries(sourceDirectory);
    if (optionalSourceEntries == null) {
      // Bail out if error enumerating files in source directory
      return false;
    }
    var sourceEntries = optionalSourceEntries.Value;
    OnEntriesToDeleteDiscovered(sourceDirectory, sourceEntries.Item);
    
    // Delete files, links and subdirectories
#if false
    var allEntriesDeleted = true;
    using var deleteSubDirTaskList = _boolTaskListPool.AllocateFrom();
    foreach (var entry in sourceEntries.Item) {
      if (entry.IsRegularDirectory) {
        deleteSubDirTaskList.Item.Add(DeleteDirectoryEntriesAsync(entry, includeFilter));
      }
    }
    if (deleteSubDirTaskList.Item.Count > 0) {
      var results = await Task.WhenAll(deleteSubDirTaskList.Item).ConfigureAwait(false);
      //TODO
      //var allDeleted = results.All(static allEntriesDeleted => allEntriesDeleted);
      //if (!allDeleted) {}
    }
    _fileSystem.Extension.DeleteDirectoryEntries(sourceDirectory, sourceEntries.Item);
#else
    var allEntriesDeleted = true;
    Task<bool>? additionalTask = null;
    {
      using var deleteSubDirTaskList = _boolTaskListPool.AllocateFrom();
      foreach (var entry in sourceEntries.Item) {
        if (entry.IsFile || entry.IsReparsePoint) {
          if (!DeleteSingleEntry(entry, includeFilter)) {
            allEntriesDeleted = false;
          }
        }
        else if (entry.IsRegularDirectory) {
          deleteSubDirTaskList.Item.Add(DeleteDirectoryAsync(entry, includeFilter));
        }
      }
      if (deleteSubDirTaskList.Item.Count > 0) {
        additionalTask = Task.WhenAll(deleteSubDirTaskList.Item).ContinueWith(static results => {
          return results.Result.All(static allEntriesDeleted => allEntriesDeleted);
        });
      }
    }
    if (additionalTask != null) {
      var additionalTaskAllEntriesDeleted = await additionalTask.ConfigureAwait(false);
      if (!additionalTaskAllEntriesDeleted) {
        allEntriesDeleted = false;
      }
    }
#endif
    OnEntriesToDeleteProcessed(sourceDirectory, sourceEntries.Item);
    return allEntriesDeleted;
  }
  
  /// <summary>
  /// Returns whether the current operation should use <see cref="PerformCloneFile"/>
  /// </summary>
  /// <param name="sourceDirectory"></param>
  /// <param name="destinationDirectory"></param>
  /// <param name="options"></param>
  /// <param name="useCloning"></param>
  /// <returns></returns>
  private bool ShouldUseCloneFile(FileSystemEntry sourceDirectory, FileSystemEntry destinationDirectory,
    CopyOptions options, bool? useCloning) {
    return useCloning ?? ((options & CopyOptions.NoClone) == 0 &&
                          _fileSystem.Extension.IsCloningSupported(sourceDirectory.Path,
                            destinationDirectory.Path));
  }

  /// <summary>
  /// Creates a directory named <paramref name="destinationPath"/>, returning <code>null</code> if an error
  /// occured.
  /// </summary>
  private FileSystemEntry? CreateDirectory(FullPath destinationPath) {
    try {
      // Create destination directory (throw if error)
      _fileSystem.CreateDirectory(destinationPath);
      var destinationDirectory = _fileSystem.GetEntry(destinationPath);
      OnDirectoryCreated(destinationDirectory);
      return destinationDirectory;
    }
    catch (Exception e) {
      OnError(destinationPath, e);
      return null;
    }
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

  /// <summary>
  /// Copy (or clone) a regular file or symlink from <paramref name="sourceEntry"/> to
  /// <paramref name="destinationDirectory"/> unless the file already exist and is the same as the source file
  /// (see <paramref name="fileComparer"/>
  ///
  /// Note: Errors are reported via <see cref="OnError"/> 
  /// </summary>
  private void PerformOrScheduleFileEntryCopy(
    FileSystemEntry sourceEntry,
    FileSystemEntry destinationDirectory,
    IFileComparer fileComparer,
    SmallSet<FileSystemEntry> destinationDirectoryEntries,
    List<Task> fileTaskList,
    bool useCloneFile) {
    Guard.CheckArgument(sourceEntry.IsFile || sourceEntry.IsReparsePoint,
      "Internal error: Only regular files and reparse points can be copied directly");

    var destinationExists = destinationDirectoryEntries.TryGet(sourceEntry, out var destinationEntry);
    var destinationPath = destinationExists
      ? destinationEntry.Path
      : new FullPath(destinationDirectory.Path, sourceEntry.Name);

    if (destinationExists) {
      try {
        var areEqual = CompareFiles(sourceEntry, destinationEntry, fileComparer);
        if (areEqual) {
          OnFileCopySkipped(sourceEntry);
          return;
        }
      } catch (Exception e) {
        // If we can't compare files, log error and bail out
        OnError(sourceEntry.Path, e);
        return;
      }
    }

    // We can only clone regular files (not directory and not symlinks)
    if (useCloneFile && sourceEntry.IsRegularFile) {
      // If file size is >= threshold, offload cloning to a background task
      if (sourceEntry.FileSize >= _largeFileAsyncThreshold) {
        fileTaskList.Add(Task.Run(() => PerformCloneFile(sourceEntry, destinationPath, dryRun: false)));
      } else {
        PerformCloneFile(sourceEntry, destinationPath, dryRun: false);
      }
    } else {
      var copyFileOptions = CopyFileOptions.Default | CopyFileOptions.NoClone;
      // If file size is >= threshold, offload copying to a background task
      if (sourceEntry.IsRegularFile && sourceEntry.FileSize >= _largeFileAsyncThreshold) {
        fileTaskList.Add(Task.Run(() => PerformCopyFile(sourceEntry, destinationPath, destinationEntry, destinationExists, copyFileOptions)));
      } else {
        PerformCopyFile(sourceEntry, destinationPath, destinationEntry, destinationExists, copyFileOptions);
      }
    }
  }

  /// <summary>
  /// Copy the contents of <paramref name="sourceEntry"/> to <paramref name="destinationPath"/>.
  ///
  /// Note: Errors are reported via <see cref="OnError"/> 
  /// </summary>
  private void PerformCopyFile(
    FileSystemEntry sourceEntry,
    FullPath destinationPath,
    FileSystemEntry? destinationEntry,
    bool destinationExists,
    CopyFileOptions copyFileOptions) {
    Guard.CheckArgument(sourceEntry.IsFile || sourceEntry.IsReparsePoint,
      "Internal error: Only regular files and reparse points can be copied directly");
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
      return;
    }
    OnFileCopied(sourceEntry, sw.Elapsed, sourceEntry.FileSize);
  }
  
  /// <summary>
  /// Create a clone of <paramref name="sourceEntry"/> in <paramref name="destinationPath"/>.
  ///
  /// Note: Errors are reported via <see cref="OnError"/> 
  /// </summary>
  private void PerformCloneFile(FileSystemEntry sourceEntry, FullPath destinationPath, bool dryRun) {
    Guard.CheckArgument(sourceEntry.IsRegularFile,
      "Internal error: Only regular files can be cloned");
    var sw = _stopwatchFactory.Create();
    OnFileCloning(sourceEntry);
    if (!dryRun) {
      try {
        _fileSystem.Extension.CloneFile(sourceEntry, destinationPath);
      } catch (Exception e) {
        OnError(sourceEntry.Path, e);
        return;
      }
    }
    OnFileCloned(sourceEntry, sw.Elapsed, sourceEntry.FileSize);
  }
  
  private void PerformOrScheduleCloneFileIfNeeded(
    FileSystemEntry sourceEntry,
    FileSystemEntry destinationEntry,
    IFileComparer fileComparer,
    bool dryRun,
    List<Task> fileTaskList) {
    Guard.CheckArgument(sourceEntry.IsRegularFile,
      "Internal error: Only regular files can be cloned");

    // If file size is >= threshold, offload copying to a background task
    if (sourceEntry.FileSize >= _largeFileAsyncThreshold) {
      fileTaskList.Add(Task.Run(() => PerformCloneFileIfNeeded(sourceEntry, destinationEntry, fileComparer, dryRun)));
    }
    else {
      PerformCloneFileIfNeeded(sourceEntry, destinationEntry, fileComparer, dryRun);
    }
  }

  /// <summary>
  /// Create a clone of <paramref name="sourceEntry"/> as  <paramref name="destinationEntry"/>.
  ///
  /// Note: Errors are reported via <see cref="OnError"/> 
  /// </summary>
  private void PerformCloneFileIfNeeded(FileSystemEntry sourceEntry, FileSystemEntry destinationEntry, IFileComparer fileComparer, bool dryRun) {
    Guard.CheckArgument(sourceEntry.IsRegularFile,
      "Internal error: Only regular files can be cloned");

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
        PerformCloneFile(sourceEntry, destinationEntry.Path, dryRun);
      }
    }
    catch (Exception e) {
      OnError(sourceEntry.Path, e);
    }
  }

  /// <summary>
  /// Compare 2 files <paramref name="sourceEntry"/> and <paramref name="destinationEntry"/>
  /// using <paramref name="fileComparer"/>. Returns whether the files are considered equal, or <code>false</code>
  /// if there is an error.
  ///
  /// Note: Throws exception if comparison fails unexpectedly 
  /// </summary>
  private bool CompareFiles(FileSystemEntry sourceEntry, FileSystemEntry destinationEntry, IFileComparer fileComparer) {
    bool areEqual;
    if (fileComparer.IsFast) {
      areEqual = fileComparer.CompareFiles(sourceEntry, destinationEntry);
    }
    else {
      var sw = _stopwatchFactory.Create();
      OnFileComparing(sourceEntry);
      var compareData = new CompareFileData(this, sw);
      areEqual = fileComparer.CompareFiles(sourceEntry, destinationEntry, compareData, _compareFileCallback);
      OnFileCompared(sourceEntry, sw.Elapsed, sourceEntry.FileSize);
    }
    return areEqual;
  }

  /// <summary>
  /// Delete a single file or reparse point if allowed by <paramref name="includeFilter"/>, returning whether
  /// <paramref name="includeFilter"/> returned <code>true</code>. 
  ///
  ///  Note: Errors are reported via <see cref="OnError"/> 
  /// </summary>
  private bool DeleteSingleEntry(FileSystemEntry entry, Func<FileSystemEntry, bool> includeFilter) {
    if (!includeFilter(entry)) {
      return false;
    }

    var sw = _stopwatchFactory.Create();
    OnEntryDeleting(entry);
    try {
      _fileSystem.DeleteEntry(entry);
    } catch (Exception e) {
      OnError(entry.Path, e);
      return false;
    }
    OnEntryDeleted(entry, sw.Elapsed);
    return true;
  }

  /// <summary>
  /// Returns the file system entries of <paramref name="directory"/>, or <code>null</code> if there was an error
  ///
  ///  Note: Errors are reported via <see cref="OnError"/> 
  /// </summary>
  private FromPool<List<FileSystemEntry>>? GetDirectoryEntries(FileSystemEntry directory) {
    try {
      return _fileSystem.GetDirectoryFiles(directory.Path);
    } catch (Exception e) {
      OnError(directory.Path, e);
      return null;
    }
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
    
  private readonly struct CopyFileData(ParallelFileSystem instance, NoAllocStopwatch stopwatch) {
    public ParallelFileSystem Instance { get; } = instance;
    public NoAllocStopwatch Stopwatch { get; } = stopwatch;
  }

  private readonly struct CompareFileData(ParallelFileSystem instance, NoAllocStopwatch stopwatch) {
    public ParallelFileSystem Instance { get; } = instance;
    public NoAllocStopwatch Stopwatch { get; } = stopwatch;
  }
}
