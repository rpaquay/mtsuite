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

namespace mtfindstr {
  public class FindStrSummaryCollector : IDirectorCollector<VoidValue> {
    private readonly FindStrProgressMonitor _progressMonitor;
    private readonly List<FindStrFileResult> _fileResults = new List<FindStrFileResult>();
    private readonly FileNameMatcher _fileNameMatcher;
    private readonly FindStrMatcher _findStrMatcher;

    public FindStrSummaryCollector(FindStrProgressMonitor progressMonitor, FileNameMatcher fileNameMatched, FindStrMatcher fileStrMatcher) {
      _progressMonitor = progressMonitor;
      _fileNameMatcher = fileNameMatched;
      _findStrMatcher = fileStrMatcher;
    }

    public List<FindStrFileResult> FileResults => _fileResults;

    public VoidValue CreateItemForDirectory(IFileSystem fileSystem, FileSystemEntry directory, int depth) {
      return VoidValue.Instance;
    }

    public ITaskCollection OnDirectoryEntriesEnumerated(IFileSystem fileSystem, VoidValue value, FileSystemEntry directory, List<FileSystemEntry> entries, ITaskFactory taskFactory) {
      var filesWithMatchingName = entries.Where(entry => entry.IsFile && _fileNameMatcher(entry)).ToList();
      if (filesWithMatchingName.Count == 0) {
        return taskFactory.EmptyCollection();
      }

      var tasks = filesWithMatchingName.Select(entry => taskFactory.StartNew(() => {
        try {
          _progressMonitor.OnFileSearched();
          var findStrEntries = _findStrMatcher(fileSystem, entry);
          AddFindStrResult(entry, findStrEntries);
        } catch (Exception e) {
          AddError(entry, e);
        }
      }));
      return taskFactory.CreateCollection(tasks);
    }

    private void AddFindStrResult(FileSystemEntry entry, IList<FindStrEntry> entries) {
      if (entries.Count > 0) {
        lock (_fileResults) {
          _fileResults.Add(new FindStrFileResult {
            Path = entry.Path,
            Entries = entries
          });
        }
        _progressMonitor.OnFileMatchFound();
      }
    }

    private void AddError(FileSystemEntry entry, Exception e) {
      _progressMonitor.OnError(entry.Path, e);
    }

    public void OnDirectoryTraversed(IFileSystem fileSystem, VoidValue parentValue, VoidValue childValue) {
    }
  }
}
