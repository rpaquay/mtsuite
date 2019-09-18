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
using mtsuite.shared;
using mtsuite.shared.FileNameMatching;
using mtsuite.shared.Tasks;

namespace mtgrep {
  public class MtGrepSummaryCollector : IDirectorCollector<VoidValue> {
    private readonly MtGrepProgressMonitor _progressMonitor;
    private readonly List<GrepFileResult> _grepResults = new List<GrepFileResult>();
    private readonly FileNameMatcher _nameMatcher;
    private readonly GrepMatcher _grepMatcher;

    public MtGrepSummaryCollector(MtGrepProgressMonitor progressMonitor, FileNameMatcher nameMatcher, GrepMatcher grepMatcher) {
      _progressMonitor = progressMonitor;
      _nameMatcher = nameMatcher;
      _grepMatcher = grepMatcher;
    }

    public List<GrepFileResult> GrepResults => _grepResults;

    public VoidValue CreateItemForDirectory(IFileSystem fileSystem, FileSystemEntry directory, int depth) {
      return VoidValue.Instance;
    }

    public ITaskCollection OnDirectoryEntriesEnumerated(IFileSystem fileSystem, VoidValue value, FileSystemEntry directory, List<FileSystemEntry> entries, ITaskFactory taskFactory) {
      var filesWithMatchingName = entries.Where(entry => entry.IsFile && _nameMatcher(entry)).ToList();
      if (filesWithMatchingName.Count == 0) {
        return taskFactory.EmptyCollection();
      }

      var tasks = filesWithMatchingName.Select(entry => taskFactory.StartNew(() => {
        try {
          _progressMonitor.OnFileSearched();
          var grepEntries = _grepMatcher(fileSystem, entry);
          AddGrepResult(entry, grepEntries);
        } catch (Exception e) {
          AddError(entry, e);
        }
      }));
      return taskFactory.CreateCollection(tasks);
    }

    private void AddGrepResult(FileSystemEntry entry, IList<GrepEntry> grepEntries) {
      if (grepEntries.Count > 0) {
        lock (_grepResults) {
          _grepResults.Add(new GrepFileResult {
            Path = entry.Path,
            Entries = grepEntries
          });
        }
        _progressMonitor.OnFileMatchFound();
      }
    }

    private void AddError(FileSystemEntry entry, Exception e) {
      _progressMonitor.OnError(e);
    }

    public void OnDirectoryTraversed(IFileSystem fileSystem, VoidValue parentValue, VoidValue childValue) {
    }
  }
}
