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

namespace mtsuite.shared.Files {
  public class LastWriteTimeFileComparer : IFileComparer {
    public bool CompareFiles(FileSystemEntry file1, FileSystemEntry file2) {
      return
        (file1.IsFile == file2.IsFile) &&
        (file1.IsDirectory == file2.IsDirectory) &&
        (file1.IsReparsePoint == file2.IsReparsePoint) &&
        (file1.FileSize == file2.FileSize) &&
        StringComparer.OrdinalIgnoreCase.Equals(file1.Name, file2.Name) &&
        DateTime.Equals(file1.LastWriteTimeUtc, file2.LastWriteTimeUtc);
    }
  }
}