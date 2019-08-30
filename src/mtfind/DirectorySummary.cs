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
using System.Linq;
using mtsuite.CoreFileSystem;

namespace mtfind {

  /// <summary>
  /// Data object used to collect summary information for a single directory.
  /// There is a disctinct object per directory to ensure that parallel processing
  /// can occur without locking, as a given directory is processed by at most one
  /// thread at any single point in time.
  /// </summary>
  public class DirectorySummary {
    private readonly FileSystemEntry _directoryEntry;
    private List<DirectorySummary> _children;
    private List<FileSystemEntry> _matchedFiles;

    public DirectorySummary(FileSystemEntry directoryEntry) {
      _directoryEntry = directoryEntry;
    }

    public void AddMatchedFile(FileSystemEntry entry) {
      if (_matchedFiles == null) {
        _matchedFiles = new List<FileSystemEntry>();
      }
      _matchedFiles.Add(entry);
    }

    public void AddChild(DirectorySummary summary) {
      if (_children == null) {
        _children = new List<DirectorySummary>();
      }
      _children.Add(summary);
    }

    /// <summary>
    /// The directory entry this instance correspond to.
    /// </summary>
    public FileSystemEntry DirectoryEntry {
      get { return _directoryEntry; }
    }

    /// <summary>
    /// The list of summaries of the child directories.
    /// </summary>
    public IEnumerable<DirectorySummary> Children {
      get { return _children ?? Enumerable.Empty<DirectorySummary>(); }
    }

    /// <summary>
    /// The list of file system entries that match the search pattern(s).
    /// </summary>
    public IEnumerable<FileSystemEntry> MatchedFiles {
      get { return _matchedFiles ?? Enumerable.Empty<FileSystemEntry>(); }
    }
  }
}