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
using System.Threading.Tasks;
using mtsuite.CoreFileSystem;
#nullable enable

namespace mtsuite.shared {
  public interface IParallelFileSystem {
    event Action Pulse;
    event Action<FileSystemEntry> EntriesToDeleteDiscovering;
    event Action<FileSystemEntry, List<FileSystemEntry>> EntriesToDeleteDiscovered;
    event Action<FileSystemEntry, List<FileSystemEntry>> EntriesToDeleteProcessed;
    event Action<FileSystemEntry> EntryDeleting;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/> EntryDeleted;
    event Action<FileSystemEntry> FileCopySkipped;
    event Action<FileSystemEntry> FileComparing;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/, long /*bytesFromPreviousCall*/, long /*bytesSoFar*/> FileComparingProgress;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/, long /*bytesTotal*/> FileCompared;
    event Action<FileSystemEntry> FileCopying;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/, long /*bytesFromPreviousCall*/, long /*bytesSoFar*/> FileCopyingProgress;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/, long /*bytesTotal*/> FileCopied;
    event Action<FileSystemEntry> FileCloning;
    event Action<FileSystemEntry, TimeSpan /* Elapsed*/, long /*bytesTotal*/> FileCloned;
    event Action<FileSystemEntry> FileCloneSkipped;
    event Action<FileSystemEntry> FileAlreadyCloned;
    event Action<FileSystemEntry> DirectoryTraversing;
    event Action<FileSystemEntry, List<FileSystemEntry>?> DirectoryTraversed;
    event Action<FileSystemEntry> DirectoryCreated;
    event Action<FullPath, Exception> Error;

    void WaitForTask(Task task);

    Task<T> TraverseDirectoryAsync<T>(FileSystemEntry directoryEntry, IDirectorCollector<T> collector, bool followLinks = false);

    Task CopyDirectoryAsync(FileSystemEntry sourceDirectory, FileSystemEntry destinationDirectory, CopyOptions options, IFileComparer fileComparer);

    /// <summary>
    /// Look for all regular files in <paramref name="sourceDirectory"/> that are in
    /// <paramref name="destinationDirectory"/>, at the same relative hierarchy, and replace the destination
    /// with a clone from the source. 
    /// </summary>
    Task CompactDirectoryAsync(FileSystemEntry sourceDirectory, FileSystemEntry destinationDirectory, IFileComparer fileComparer, bool dryRun);

    /// <summary>
    /// Delete a file system entry recursively the entry is a directory.
    /// Directories are deleted only if <paramref name="includeFilter"/> matches all files in a given
    /// directory. Returns whether all files and directories were deleted.
    /// </summary>
    Task<bool> DeleteEntryAsync(FileSystemEntry entry, Func<FileSystemEntry, bool> includeFilter);
  }

  /// <summary>
  /// Interface implemented by callers of <see cref="IParallelFileSystem.TraverseDirectoryAsync{T}"/>
  /// </summary>
  /// <typeparam name="T">The implementation specific element used to track
  /// directories</typeparam>
  public interface IDirectorCollector<T> {
    /// <summary>
    /// Create a collector item for the given <paramref name="directory"/>
    /// </summary>
    T CreateItemForDirectory(IFileSystem fileSystem, FileSystemEntry directory, int depth);

    /// <summary>
    /// Called when a child entry of the directory corresponding to <paramref
    /// name="item"/> has been enumerated.
    /// </summary>
    Action? OnDirectoryEntryEnumerated(IFileSystem fileSystem, T item, FileSystemEntry directory, FileSystemEntry entry);

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