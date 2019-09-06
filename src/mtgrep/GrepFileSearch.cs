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
using System.Collections.ObjectModel;
using System.IO;

using mtsuite.CoreFileSystem;

namespace mtgrep {
  public partial class GrepFileSearch {
    private readonly string _pattern;
    private readonly IList<GrepEntry> _emptyResult = new ReadOnlyCollection<GrepEntry>(new List<GrepEntry>());

    public GrepFileSearch(string pattern) {
      _pattern = pattern;
    }

    public IList<GrepEntry> SearchFile(IFileSystem fileSystem, FileSystemEntry entry) {
      if (!entry.IsFile) {
        return _emptyResult;
      }

      // Skip small files
      if (_pattern.Length > entry.FileSize) {
        return _emptyResult;
      }

      // Create collection lazily in case there are no matches
      using (var stream = fileSystem.OpenFile(entry.Path, FileAccess.Read)) {
        return SearchStream(stream, entry);
      }
    }

    public IList<GrepEntry> SearchStream(FileStream stream, FileSystemEntry entry) {
      var grepStream = new GrepFileStream(stream);
      if (grepStream.IsBinary()) {
        return _emptyResult;
      }

      // Reset stream
      stream.Position = 0;

      // Create collection lazily in case there are no matches
      IList<GrepEntry> result = null;
      using (var reader = new StreamReader(stream)) {
        int lineNumber = 0;
        long currentOffset = stream.Position;
        for (string line = reader.ReadLine(); line != null; line = reader.ReadLine()) {
          if (line.IndexOf(_pattern) >= 0) {
            // Create collection lazily in case there are no matches
            if (result == null) {
              result = new List<GrepEntry>(); ;
            }
            result.Add(new GrepEntry() {
              TextExtract = line,
              LineNumber = lineNumber,
              StartOffset = currentOffset,
              EndOffset = currentOffset + line.Length
            });
          }
          lineNumber++;
        }
      }
      return result ?? _emptyResult;
    }
  }
}
