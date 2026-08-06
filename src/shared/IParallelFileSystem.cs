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
using System.Threading.Tasks;
using mtsuite.CoreFileSystem;

namespace mtsuite.shared {
  public interface IParallelFileSystem {
    event Action Pulse;
    event Action<FileSystemEntry> EntriesDiscovering;
    event Action<FileSystemEntry, List<FileSystemEntry>> EntriesDiscovered;
    event Action<FileSystemEntry> EntriesToDeleteDiscovering;
    event Action<FileSystemEntry, List<FileSystemEntry>> EntriesToDeleteDiscovered;
    event Action<FileSystemEntry, List<FileSystemEntry>> EntriesToDeleteProcessed;
    event Action<FileSystemEntry> EntryDeleting;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/> EntryDeleted;
    event Action<FileSystemEntry> FileCopySkipped;
    event Action<FileSystemEntry> FileCopying;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/, long /*bytesThisChunk*/> FileCopyingProgress;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/, long /*bytesTotal*/> FileCopied;
    event Action<FileSystemEntry> FileCompacting;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/, long /*bytesTotal*/> FileCompacted;
    event Action<FileSystemEntry> FileCompactSkipped;
    event Action<FileSystemEntry> DirectoryTraversing;
    event Action<FileSystemEntry> DirectoryTraversed;
    event Action<FileSystemEntry> DirectoryCreated;
    event Action<FullPath, Exception> Error;

    void WaitForTask(Task task);

    Task<T> TraverseDirectoryAsync<T>(FileSystemEntry directoryEntry, IDirectorCollector<T> collector, bool followLinks = false);

    Task CopyDirectoryAsync(FileSystemEntry sourceDirectory, FullPath destinationPath, CopyOptions options, IFileComparer fileComparer, bool destinationDirectoryIsNew);

    Task CompactDirectoryAsync(FileSystemEntry sourceDirectory, FullPath destinationPath, IFileComparer fileComparer, bool dryRun = false);

    /// <summary>
    /// Delete a file system entry. Recurse through directories if
    /// the entry is a directory.
    /// </summary>
    Task DeleteEntryAsync(FileSystemEntry entry);
  }


  /// <summary>
  /// Interface implemented by callers of <see
  /// cref="IParallelFileSystem.TraverseDirectoryAsync{T}"/>
  /// </summary>
  /// <typeparam name="T">The implementation specific element used to track
  /// directories</typeparam>
  public interface IDirectorCollector<T> {
    /// <summary>
    /// Create a collector item for the given <paramref name="directory"/>
    /// </summary>
    T CreateItemForDirectory(IFileSystem fileSystem, FileSystemEntry directory, int depth);

    /// <summary>
    /// Called when children entries of the directory corresponding to <paramref
    /// name="item"/> have been enumerated.
    /// </summary>
    Task OnDirectoryEntriesEnumerated(IFileSystem fileSystem, T item, FileSystemEntry directory, List<FileSystemEntry> children);

    /// <summary>
    /// Called after a sub-directory <paramref name="childItem"/> of the
    /// directory <paramref name="parentItem"/> has been processed.
    /// </summary>
    void OnDirectoryTraversed(IFileSystem fileSystem, T parentItem, T childItem);
  }

  [Flags]
  public enum CopyOptions {
    None,
    SkipIdenticalFiles = 0x01,
    DeleteExtraFiles = 0x02,
    DeleteMismatchedFiles = 0x04,
    NoClone = 0x08,
  }
}