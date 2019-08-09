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

using System.Collections.Generic;

using mtsuite.CoreFileSystem;

namespace mtfind {
  public class DirectorySummaryRoot {
    public DirectorySummary Summary { get; set; }
  }

  public class DirectorySummary {
    private readonly FileSystemEntry _directoryEntry;
    private readonly List<DirectorySummary> _children;
    private readonly List<FileSystemEntry> _matchedFiles;

    public DirectorySummary(FileSystemEntry directoryEntry) {
      _directoryEntry = directoryEntry;
      _children = new List<DirectorySummary>();
      _matchedFiles = new List<FileSystemEntry>();
    }

    public FileSystemEntry DirectoryEntry {
      get { return _directoryEntry; }
    }

    public List<DirectorySummary> Children {
      get { return _children; }
    }

    public List<FileSystemEntry> MatchedFiles {
      get {
        return _matchedFiles;
      }
    }
  }
}