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
using mtsuite.shared.FileNameMatching;
using mtsuite.shared.Tasks;
using System;

namespace mtgrep {
  public delegate IList<GrepEntry> GrepMatcher(IFileSystem fileSystem, FileSystemEntry entry);

  public class GrepEntry {
    public int LineNumber { get; set; }
    public long StartOffset { get; set; }
    public long EndOffset { get; set; }
    public string TextExtract { get; set; }
  }

  public class GrepFileResult {
    public FullPath Path { get; set; }
    public IList<GrepEntry> Entries { get; set; }
  }
  public class ErrorEntry {
    public FullPath Path { get; set; }
    public Exception Error { get; set; }
  }


  public class MtGrepSummaryCollector : IDirectorCollector<VoidValue> {
    private readonly List<GrepFileResult> _grepResults = new List<GrepFileResult>();
    private readonly List<ErrorEntry> _errors = new List<ErrorEntry>();
    private readonly FileNameMatcher _nameMatcher;
    private readonly GrepMatcher _grepMatcher;

    public MtGrepSummaryCollector(FileNameMatcher nameMatcher, GrepMatcher grepMatcher) {
      _nameMatcher = nameMatcher;
      _grepMatcher = grepMatcher;
    }

    public List<GrepFileResult> GrepResults => _grepResults;

    public List<ErrorEntry> Errors => _errors;

    public VoidValue CreateItemForDirectory(IFileSystem fileSystem, FileSystemEntry directory, int depth) {
      return VoidValue.Instance;
    }

    public ITaskCollection OnDirectoryEntriesEnumerated(IFileSystem fileSystem, VoidValue value, FileSystemEntry directory, List<FileSystemEntry> entries, ITaskFactory taskFactory) {
      foreach (var entry in entries) {
        if (_nameMatcher(entry)) {
          try {
            var grepEntries = _grepMatcher(fileSystem, entry);
            if (grepEntries.Count > 0) {
              lock (_grepResults) {
                _grepResults.Add(new GrepFileResult {
                  Path = entry.Path,
                  Entries = grepEntries
                });
              }
            }
          } catch (Exception e) {
            AddError(entry, e);
          }
        }
      }
      return taskFactory.EmptyCollection();
    }

    private void AddError(FileSystemEntry entry, Exception e) {
      lock (_errors) {
        _errors.Add(new ErrorEntry() { Path = entry.Path, Error = e });
      }
    }

    public void OnDirectoryTraversed(IFileSystem fileSystem, VoidValue parentValue, VoidValue childValue) {
    }
  }
}
