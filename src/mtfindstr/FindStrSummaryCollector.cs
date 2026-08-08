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
using System.Linq;
using System.Threading.Tasks;
using mtsuite.CoreFileSystem;
using mtsuite.shared;
using mtsuite.shared.FileNameMatching;

namespace mtfindstr {
  public class FindStrSummaryCollector : IDirectorCollector<VoidValue> {
    private readonly FindStrProgressMonitor _progressMonitor;
    private readonly List<FindStrFileResult> _fileResults = new List<FindStrFileResult>();
    private readonly IList<FileNameMatcher> _fileNameMatchers;
    private readonly FindStrMatcher _findStrMatcher;

    public FindStrSummaryCollector(FindStrProgressMonitor progressMonitor, IList<FileNameMatcher> fileNameMatched, FindStrMatcher fileStrMatcher) {
      _progressMonitor = progressMonitor;
      _fileNameMatchers = fileNameMatched;
      _findStrMatcher = fileStrMatcher;
    }

    public List<FindStrFileResult> FileResults => _fileResults;

    public VoidValue CreateItemForDirectory(IFileSystem fileSystem, FileSystemEntry directory, int depth) {
      return VoidValue.Instance;
    }

    public Task OnDirectoryEntriesEnumerated(IFileSystem fileSystem, VoidValue value, FileSystemEntry directory, List<FileSystemEntry> entries) {
      var filesWithMatchingName = entries.Where(entry => MatchesFileName(entry)).ToList();
      if (filesWithMatchingName.Count == 0) {
        return Task.CompletedTask;
      }

      var tasks = filesWithMatchingName.Select(entry => Task.Run(() => {
        _progressMonitor.OnFileSearching(entry);
        try {
          var findStrEntries = _findStrMatcher(fileSystem, entry);
          AddFindStrResult(entry, findStrEntries);
        } catch (Exception e) {
          AddError(entry, e);
        } finally {
          _progressMonitor.OnFileSearched(entry);
        }
      }));
      return Task.WhenAll(tasks);
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

    bool MatchesFileName(FileSystemEntry entry) {
      return entry.IsFile && _fileNameMatchers.Any(matcher => matcher(entry));
    }

    private void AddError(FileSystemEntry entry, Exception e) {
      _progressMonitor.OnError(entry.Path, e);
    }

    public void OnDirectoryTraversed(IFileSystem fileSystem, VoidValue parentValue, VoidValue childValue) {
    }
  }
}
