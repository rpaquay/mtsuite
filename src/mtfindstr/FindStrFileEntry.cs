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
using System.IO;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtfindstr {
  public class FindStrFileEntry {
    private readonly string _pattern;
    private readonly char[] _patternArray;
    private readonly IPool<FindStrStream> _streamPool;
    private readonly IPool<IList<FindStrEntry>> _listPool;

    public FindStrFileEntry(string pattern, MtPoolFactory poolFactory) {
      _pattern = pattern;
      _patternArray = pattern.ToCharArray();
      _streamPool = poolFactory.Create("FindStrStream", () => new FindStrStream());
      _listPool = poolFactory.Create<IList<FindStrEntry>>("FindStrEntries", () => new List<FindStrEntry>(), static list => list.Clear());
    }

    public FromPool<IList<FindStrEntry>> SearchFile(IFileSystem fileSystem, FileSystemEntry entry) {
      if (!entry.IsFile) {
        return _listPool.AllocateFrom();
      }

      // Skip small files
      if (_pattern.Length > entry.FileSize) {
        return _listPool.AllocateFrom();
      }

      using (var stream = fileSystem.OpenFile(entry.Path, FileAccess.Read)) {
        using (var reader = new StreamReader(stream)) {
          using (var findStrStream = _streamPool.AllocateFrom()) {
            return findStrStream.Item.Search(reader, _patternArray, _listPool);
          }
        }
      }
    }
  }
}
