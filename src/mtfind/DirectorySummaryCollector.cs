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
using mtsuite.shared;
using mtsuite.CoreFileSystem;

namespace mtfind {
  public class DirectorySummaryCollector : IDirectorCollector<VoidValue> {
    private readonly List<FileSystemEntry> _matchedFiles = new List<FileSystemEntry>();
    private readonly FileNameMatcher _nameMatcher;

    public DirectorySummaryCollector(FileNameMatcher nameMatcher) {
      _nameMatcher = nameMatcher;
    }

    public List<FileSystemEntry> MatchedFiles => _matchedFiles;

    public VoidValue CreateItemForDirectory(FileSystemEntry directory, int depth) {
      return VoidValue.Instance;
    }

    public void OnDirectoryEntriesEnumerated(VoidValue value, FileSystemEntry directory, List<FileSystemEntry> entries) {
      foreach (var entry in entries) {
        if (_nameMatcher(entry)) {
          lock (_matchedFiles) {
            _matchedFiles.Add(entry);
          }
        }
      }
    }

    public void OnDirectoryTraversed(VoidValue parentValue, VoidValue childValue) {
    }
  }
}
