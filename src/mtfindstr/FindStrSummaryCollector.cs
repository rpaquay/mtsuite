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
#nullable enable

using System;
using System.Collections.Generic;
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

    public void OnDirectoryEntryEnumerated(IFileSystem fileSystem, VoidValue value, FileSystemEntry directory, FileSystemEntry entry) {
      if (!MatchesFileName(entry)) {
        return;
      }

      _progressMonitor.OnFileSearching(entry);
      try {
        using var findStrEntries = _findStrMatcher(fileSystem, entry);
        if (findStrEntries.Item.Count > 0) {
          AddFindStrResult(entry, findStrEntries.Item);
        }
      }
      catch (Exception e) {
        AddError(entry, e);
      }
      finally {
        _progressMonitor.OnFileSearched(entry);
      }
    }

    private void AddFindStrResult(FileSystemEntry entry, IList<FindStrEntry> entries) {
      lock (_fileResults) {
        _fileResults.Add(new FindStrFileResult {
          Path = entry.Path,
          Entries = new List<FindStrEntry>(entries)
        });
      }
      _progressMonitor.OnFileMatchFound(entry, entries);
    }

    bool MatchesFileName(FileSystemEntry entry) {
      if (!entry.IsFile) {
        return false;
      }
      foreach (var matcher in _fileNameMatchers) {
        if (matcher(entry)) {
          return true;
        }
      }
      return false;
    }

    private void AddError(FileSystemEntry entry, Exception e) {
      _progressMonitor.OnError(entry.Path, e);
    }

    public void OnDirectoryTraversed(IFileSystem fileSystem, VoidValue parentValue, VoidValue childValue) {
    }
  }
}
