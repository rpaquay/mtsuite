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
using mtsuite.shared.Collections;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;
using mtsuite.shared.Tasks;

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
    private readonly ITaskFactory _taskFactory;
    private readonly INoAllocStopwatchFactory _stopwatchFactory;
    private readonly IPool<List<FileSystemEntry>> _entryListPool = new ListPool<FileSystemEntry>();

    private readonly IPool<SmallSet<FileSystemEntry>> _entrySetPool =
      PoolFactory<SmallSet<FileSystemEntry>>.Create(
        () => new SmallSet<FileSystemEntry>(FileSystemEntryNameComparer.Instance),
        x => x.Clear());

    /// <summary>
    /// Callback to <see cref="IFileSystem.CopyFile"/>, stored in a field to avoid GC allocation
    /// at every invocation.
    /// </summary>
    private readonly CopyFileCallback<CopyFileData> _copyFileCallback = static (ref FileSystemEntry sourceEntry, long copiedBytes, long totalBytes, ref CopyFileData data) => {
      data.Instance.OnFileCopyingProgress(sourceEntry, data.Stopwatch.Elapsed, copiedBytes);
    };

    public ParallelFileSystem(
      IFileSystem fileSystem,
      ITaskFactory taskFactory = null,
      INoAllocStopwatchFactory stopwatchFactory = null,
      bool parallelFileCopy = false) {
      _fileSystem = fileSystem;
      _taskFactory = taskFactory ?? new DefaultTaskFactory();
      _stopwatchFactory = stopwatchFactory ?? NoAllocStopwatchFactory.Instance;
      ParallelFileCopy = parallelFileCopy;
    }

    public bool ParallelFileCopy { get; set; }

    public event Action<FullPath, Exception> Error;
    public event Action Pulse;
    public event Action<FileSystemEntry> EntriesDiscovering;
    public event Action<FileSystemEntry, List<FileSystemEntry>> EntriesDiscovered;
    public event Action<FileSystemEntry> EntriesToDeleteDiscovering;
    public event Action<FileSystemEntry, List<FileSystemEntry>> EntriesToDeleteDiscovered;
    public event Action<FileSystemEntry, List<FileSystemEntry>> EntriesToDeleteProcessed;
    public event Action<FileSystemEntry> EntryDeleting;
    public event Action<FileSystemEntry, TimeSpan> EntryDeleted;
    public event Action<FileSystemEntry> FileCopySkipped;
    public event Action<FileSystemEntry> FileCopying;
    public event Action<FileSystemEntry, TimeSpan, long> FileCopyingProgress;
    public event Action<FileSystemEntry, TimeSpan, long> FileCopied;
    public event Action<FileSystemEntry> DirectoryTraversing;
    public event Action<FileSystemEntry> DirectoryTraversed;
    public event Action<FileSystemEntry> DirectoryCreated;

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

    public void WaitForTask(ITask task) {
      while (true) {
        var completed = task.Wait(TimeSpan.FromMilliseconds(50));
        if (completed)
          break;
        OnPulse();
      }
    }

    public ITask<T> TraverseDirectoryAsync<T>(FileSystemEntry directoryEntry, IDirectorCollector<T> collector, bool followLinks = false) {
      return TraverseDirectoryAsync<T>(directoryEntry, collector, followLinks, 0, true);
    }

    private ITask<T> TraverseDirectoryAsync<T>(
      FileSystemEntry directoryEntry,
      IDirectorCollector<T> collector,
      bool followLinks,
      int depth,
      bool skipNotification) {

      return _taskFactory.StartNew(() => {
        if (!skipNotification) {
          OnDirectoryTraversing(directoryEntry);
        }
        return TraverseDirectoryEntriesAsync(
          directoryEntry,
          collector,
          followLinks,
          depth);
      }).Then(t => {
        if (!skipNotification)
          OnDirectoryTraversed(directoryEntry);
        return t.Result;
      });
    }

    private ITask<T> TraverseDirectoryEntriesAsync<T>(
      FileSystemEntry directoryEntry,
      IDirectorCollector<T> collector,
      bool followLinks,
      int depth) {

      OnEntriesDiscovering(directoryEntry);
      var entries = GetDirectoryEntries(directoryEntry.Path);
      OnEntriesDiscovered(directoryEntry, entries.Item);

      // Notify collector
      var collectorItem = collector.CreateItemForDirectory(_fileSystem, directoryEntry, depth);
      var additionalTasks = collector.OnDirectoryEntriesEnumerated(_fileSystem, collectorItem, directoryEntry, entries.Item, _taskFactory);

      // Create tasks for children directories
      var childDirectoriesTasks = _taskFactory.CreateCollection<T>();
      foreach (var entry in entries.Item) {
        if (entry.IsDirectory) {
          bool isRealDirectory = !entry.IsReparsePoint;
          bool followDirectoryLink = entry.IsReparsePoint && followLinks;
          if (isRealDirectory || followDirectoryLink) {
            var directoryTask = TraverseDirectoryAsync(entry, collector, followLinks, depth + 1, false);
            childDirectoriesTasks.Add(directoryTask);
          }
        }
      }

      entries.Dispose();

      // Run "additionalTasks", then run "childDirectoryTasks" then return "collectorItem"
      return additionalTasks.Then(tasks => {
        return childDirectoriesTasks.ContinueWith(tasks2 => {
          foreach (var task in tasks2) {
            collector.OnDirectoryTraversed(_fileSystem, collectorItem, task.Result);
          }
          return collectorItem;
        });
      });
    }

    public ITask CopyDirectoryAsync(
      FileSystemEntry sourceDirectory,
      FullPath destinationPath,
      CopyOptions options,
      IFileComparer fileComparer,
      bool destinationDirectoryIsNew) {
      return CopyDirectoryAsync(sourceDirectory, destinationPath, options, fileComparer, destinationDirectoryIsNew, true);
    }

    private ITask CopyDirectoryAsync(
      FileSystemEntry sourceDirectory,
      FullPath destinationPath,
      CopyOptions options,
      IFileComparer fileComparer,
      bool destinationDirectoryIsNew,
      bool skipNotification) {

      var t = _taskFactory.StartNew(() => {
        if (!skipNotification)
          OnDirectoryTraversing(sourceDirectory);
        // This happens if destination directory can't be created.
        return CopyDirectoryEntriesAsync(
          sourceDirectory,
          destinationPath,
          options,
          fileComparer,
          destinationDirectoryIsNew);
      }).Then(task => {
        if (!skipNotification)
          OnDirectoryTraversed(sourceDirectory);
        return task.Result;
      });

      return t;
    }

    private ITask CopyDirectoryEntriesAsync(
      FileSystemEntry sourceDirectory,
      FullPath destinationPath,
      CopyOptions options,
      IFileComparer fileComparer,
      bool destinationDirectoryIsNew) {

      // Ensure destination directory is created (or exists)
      var destinationDirectoryOpt = GetOrCreateDirectory(destinationPath, destinationDirectoryIsNew);
      if (destinationDirectoryOpt == null)
        return _taskFactory.CompletedTask;
      var destinationDirectory = destinationDirectoryOpt.Value;

      OnEntriesDiscovering(sourceDirectory);
      var sourceReadSuccess = TryGetDirectoryEntries(sourceDirectory.Path, out var sourceEntries);
      OnEntriesDiscovered(sourceDirectory, sourceEntries.Item);

      // If source directory could not be read, do NOT proceed with deleting destination files.
      if (!sourceReadSuccess) {
        sourceEntries.Dispose();
        return _taskFactory.CompletedTask;
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

      try {
        OnEntriesToDeleteDiscovering(destinationDirectory);
        var entriesToDelete = ComputeDestinationEntriesToDelete(sourceEntries.Item, destinationEntries.Item, options);
        OnEntriesToDeleteDiscovered(destinationDirectory, entriesToDelete);

        var deleteTasks = _taskFactory.CreateCollection(entriesToDelete.Select(entry => DeleteEntryAsync(entry)));
        return deleteTasks
          .Then(t => {
                var copySubDirectoriesTasks = _taskFactory.CreateCollection(sourceEntries.Item
                  .Where(entry => entry.IsDirectory && !entry.IsReparsePoint)
                  .Select(sourceEntry => {
                    var destinationEntryPath = destinationDirectory.Path.Combine(sourceEntry.Name);
                    var isNewDestination = !destinationSet.Item.Contains(sourceEntry);
                    return CopyDirectoryAsync(sourceEntry, destinationEntryPath, options, fileComparer, isNewDestination, false/*skipNotification*/);
                  }));

                return copySubDirectoriesTasks
                  .ContinueWith(_ => {
                    CopyFileEntries(sourceEntries.Item, destinationDirectory, fileComparer, destinationSet.Item);
                    sourceEntries.Dispose();
                    destinationEntries.Dispose();
                    destinationSet.Dispose();
                  });
            
            // var subDirEntries = new List<FileSystemEntry>();
            // var fileEntries = new List<FileSystemEntry>();
            // foreach (var entry in sourceEntries.Item) {
            //   if (entry.IsDirectory && !entry.IsReparsePoint) {
            //     subDirEntries.Add(entry);
            //   } else if (entry.IsFile || entry.IsReparsePoint) {
            //     fileEntries.Add(entry);
            //   }
            // }
            //
            // var copySubDirectoriesTasks = _taskFactory.CreateCollection(subDirEntries
            //   .Select(sourceEntry => {
            //     var destinationEntryPath = destinationDirectory.Path.Combine(sourceEntry.Name);
            //     var isNewDestination = !destinationSet.Item.Contains(sourceEntry);
            //     return CopyDirectoryAsync(sourceEntry, destinationEntryPath, options, fileComparer, isNewDestination, false/*skipNotification*/);
            //   }));
            //
            // ITask copyFilesTask = ParallelFileCopy
            //   ? _taskFactory.CreateCollection(fileEntries
            //       .Select(fileEntry => _taskFactory.StartNew(() => {
            //         CopyFileEntry(fileEntry, destinationDirectory, fileComparer, destinationSet.Item);
            //       }))).ContinueWith(_ => { })
            //   : _taskFactory.StartNew(() => {
            //       foreach (var fileEntry in fileEntries) {
            //         CopyFileEntry(fileEntry, destinationDirectory, fileComparer, destinationSet.Item);
            //       }
            //     });
            //
            // return copySubDirectoriesTasks
            //   .Then(_ => copyFilesTask.ContinueWith(__ => {
            //     sourceEntries.Dispose();
            //     destinationEntries.Dispose();
            //     destinationSet.Dispose();
            //   }));
          });
      } catch {
        // sourceEntries.Dispose();
        // destinationEntries.Dispose();
        // destinationSet.Dispose();
        throw;
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
        OnError(destinationPath,  e);
        // If we can't find the destination entry, give up this directory.
        return null;
      }

      if (directoryCreated)
        OnDirectoryCreated(destinationDirectory);
      return destinationDirectory;
    }

    private static List<FileSystemEntry> ComputeDestinationEntriesToDelete(
      List<FileSystemEntry> sourceEntries,
      List<FileSystemEntry> destinationEntries,
      CopyOptions options) {

      if (destinationEntries.Count == 0)
        return destinationEntries;

      var entriesToDelete = new List<FileSystemEntry>();

      // Note: DeleteExtraFiles is a strict superset of DeleteMismatchedFiles
      if ((options & CopyOptions.DeleteExtraFiles) != 0) {
        // Delete files in destination that are either not present in source, or
        // present in source but with a different kind (e.g. file vs directory).
        var extraEntries = destinationEntries.Except(sourceEntries, FileSystemEntryNameComparer.Instance);
        entriesToDelete.AddRange(extraEntries);
      } else if ((options & CopyOptions.DeleteMismatchedFiles) != 0) {
        // Fast O(N) lookup instead of O(N*M) nested loop
        var sourceDict = new Dictionary<string, FileSystemEntry>(sourceEntries.Count, PathHelpers.FileNameComparer);
        foreach (var src in sourceEntries) {
          sourceDict.TryAdd(src.Name , src);
        }

        foreach (var dst in destinationEntries) {
          if (sourceDict.TryGetValue(dst.Name, out var src)) {
            // Same name, different "kind"?
            if (dst.IsFile != src.IsFile ||
                dst.IsDirectory != src.IsDirectory ||
                dst.IsReparsePoint != src.IsReparsePoint) {
              entriesToDelete.Add(dst);
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

    private void CopyFileEntries(
      List<FileSystemEntry> sourceEntries,
      FileSystemEntry destinationDirectory,
      IFileComparer fileComparer,
      SmallSet<FileSystemEntry> destinationSet) {
      foreach (var entry in sourceEntries) {
        CopyFileEntry(entry, destinationDirectory, fileComparer, destinationSet);
      }
    }
    
    private void CopyFileEntry(
      FileSystemEntry sourceEntry,
      FileSystemEntry destinationDirectory,
      IFileComparer fileComparer,
      SmallSet<FileSystemEntry> destinationSet) {

      var destinationExists = destinationSet.TryGet(sourceEntry, out var destinationEntry);
      var destinationPath = destinationExists
        ? destinationEntry.Path
        : destinationDirectory.Path.Combine(sourceEntry.Name);

      if (sourceEntry.IsFile || sourceEntry.IsReparsePoint) {
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

        var sw = _stopwatchFactory.Create();
        OnFileCopying(sourceEntry);
        try {
          var copyData = new CopyFileData(this, sw); 
          if (destinationExists) {
            //_fileSystem.CopyFile(sourceEntry, destinationEntry, CopyFileOptions.Default, copyData, CallbackDelegate);
            _fileSystem.CopyFile(sourceEntry, destinationEntry, CopyFileOptions.Default, copyData, _copyFileCallback);
          } else {
            _fileSystem.CopyFile(sourceEntry, destinationPath, CopyFileOptions.Default, copyData, _copyFileCallback);
          }
        } catch (Exception e) {
          OnError(sourceEntry.Path, e);
        }
        OnFileCopied(sourceEntry, sw.Elapsed, sourceEntry.FileSize);
      }
    }

    /// <summary>
    /// Delete a file system entry. Recurse through directories if
    /// the entry is a directory.
    /// </summary>
    public ITask DeleteEntryAsync(FileSystemEntry entry) {
      return DeleteEntryAsync(entry, _ => true);
    }

    /// <summary>
    /// Delete a file system entry. Recurse through directories if
    /// the entry is a directory.
    /// </summary>
    public ITask DeleteEntryAsync(FileSystemEntry entry, Func<FileSystemEntry, bool> includeFilter) {
      if (entry.IsFile || entry.IsReparsePoint)
        return _taskFactory.StartNew(() => DeleteSingleEntry(entry, includeFilter));
      return DeleteDirectoryAsync(entry, includeFilter);
    }

    private ITask DeleteDirectoryAsync(FileSystemEntry directoryEntry, Func<FileSystemEntry, bool> includeFilter) {
      return DeleteDirectoryEntriesAsync(directoryEntry, includeFilter)
        .ContinueWith(t => DeleteSingleEntry(directoryEntry, includeFilter));
    }

    private ITask DeleteDirectoryEntriesAsync(FileSystemEntry directoryEntry, Func<FileSystemEntry, bool> includeFilter) {
      OnEntriesToDeleteDiscovering(directoryEntry);
      if (!TryGetDirectoryEntries(directoryEntry.Path, out var entries)) {
        return _taskFactory.CompletedTask;
      }

      OnEntriesToDeleteDiscovered(directoryEntry, entries.Item);
      var tasks = _taskFactory.CreateCollection(entries.Item
        .Where(entry => entry.IsDirectory && !entry.IsReparsePoint)
        .Select(entry => DeleteDirectoryEntriesAsync(entry, includeFilter)));

      return tasks.ContinueWith(_ => {
        DeleteEntries(entries.Item, includeFilter);
        OnEntriesToDeleteProcessed(directoryEntry, entries.Item);
        entries.Dispose();
      });
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