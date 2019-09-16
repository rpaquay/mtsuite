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
using System.IO;
using System.Threading;

using mtsuite.CoreFileSystem;

namespace mtgrep {
  public class GrepFileEntry {
    private readonly string _pattern;
    private readonly char[] _patternArray;
    private static readonly ThreadLocal<GrepStream> _grepSearch = new ThreadLocal<GrepStream>(() => new GrepStream());

    public GrepFileEntry(string pattern) {
      _pattern = pattern;
      _patternArray = pattern.ToCharArray();
    }

    public IList<GrepEntry> SearchFile(IFileSystem fileSystem, FileSystemEntry entry) {
      if (!entry.IsFile) {
        return GrepStream.EmptyResult;
      }

      // Skip small files
      if (_pattern.Length > entry.FileSize) {
        return GrepStream.EmptyResult;
      }

      using (var stream = fileSystem.OpenFile(entry.Path, FileAccess.Read)) {
        using (var reader = new StreamReader(stream)) {
          return _grepSearch.Value.Search(reader, _patternArray);
        }
      }
    }
  }
}
