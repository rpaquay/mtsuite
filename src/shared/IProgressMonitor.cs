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
using mtsuite.CoreFileSystem;

  #nullable enable

namespace mtsuite.shared {
  public interface IProgressMonitor<TStatistics> where TStatistics : Statistics {
    FullPath? SourcePath { get; set; }
    FullPath? DestinationPath { get; set; }

    #region single threaded methods
    void Start();
    void Pulse();
    void Stop();

    TStatistics GetStatistics();
    #endregion

    #region multi-threaded methods

    void OnEntriesToDeleteDiscovered(FileSystemEntry directory, List<FileSystemEntry> entries);
    void OnEntriesDiscovered(FileSystemEntry directory, List<FileSystemEntry> entries);

    void OnDirectoryTraversing(FileSystemEntry directory);
    void OnDirectoryTraversed(FileSystemEntry directory);

    void OnEntryDeleting(FileSystemEntry entry);
    void OnEntryDeleted(FileSystemEntry entry, TimeSpan elapsed);

    void OnDirectoryCreated(FileSystemEntry entry);

    void OnFileSkipped(FileSystemEntry sourceEntry, long size);

    void OnFileCopying(FileSystemEntry entry);
    void OnFileCopyingProgress(FileSystemEntry entry, TimeSpan elapsed, long bytesThisChunk);
    void OnFileCopied(FileSystemEntry entry, TimeSpan elapsed, long bytesTotal);

    void OnError(FullPath path, Exception e);
    #endregion
  }
}