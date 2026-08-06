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

using System.Collections.Generic;
using System.Threading.Tasks;
using mtsuite.shared;
using mtsuite.CoreFileSystem;
using mtsuite.shared.FileNameMatching;

namespace mtfind {
  public class DirectorySummaryCollector : IDirectorCollector<VoidValue> {
    private readonly List<FileSystemEntry> _matchedFiles = new List<FileSystemEntry>();
    private readonly FindProgressMonitor _progressMonitor;
    private readonly FileNameMatcher _nameMatcher;

    public DirectorySummaryCollector(FindProgressMonitor progressMonitor, FileNameMatcher nameMatcher) {
      _progressMonitor = progressMonitor;
      _nameMatcher = nameMatcher;
    }

    public List<FileSystemEntry> MatchedFiles => _matchedFiles;

    public VoidValue CreateItemForDirectory(IFileSystem fileSystem, FileSystemEntry directory, int depth) {
      return VoidValue.Instance;
    }

    public Task OnDirectoryEntriesEnumerated(IFileSystem fileSystem, VoidValue value, FileSystemEntry directory, List<FileSystemEntry> entries) {
      foreach (var entry in entries) {
        if (_nameMatcher(entry)) {
          lock (_matchedFiles) {
            _matchedFiles.Add(entry);
            _progressMonitor.OnFileMatchFound();
          }
        }
      }
      return Task.CompletedTask;
    }

    public void OnDirectoryTraversed(IFileSystem fileSystem, VoidValue parentValue, VoidValue childValue) {
    }
  }
}
