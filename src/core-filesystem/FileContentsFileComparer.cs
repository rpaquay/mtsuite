// Copyright 2018 Renaud Paquay All Rights Reserved.
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
using System.Diagnostics;
using System.IO;

namespace mtsuite.CoreFileSystem {
  public class FileContentsFileComparer : IFileComparer {
    private readonly IFileSystem _fileSystem;

    public FileContentsFileComparer(IFileSystem fileSystem) {
      _fileSystem = fileSystem;
    }

    public bool CompareFiles(FileSystemEntry file1, FileSystemEntry file2) {
      var sameKind =
        (file1.IsFile == file2.IsFile) &&
        (file1.IsDirectory == file2.IsDirectory) &&
        (file1.IsReparsePoint == file2.IsReparsePoint);
      if (!sameKind) {
        return false;
      }

      // If not same size, not equal
      if (file1.FileSize != file2.FileSize) {
        return false;
      }

      // We only need to compare the names, as we know the parent directory
      // are equivalent (although not same paths).
      if (!StringComparer.OrdinalIgnoreCase.Equals(file1.Name, file2.Name)) {
        return false;
      }

      if (file1.IsReparsePoint) {
        Debug.Assert(file2.IsReparsePoint);
        var info1 = _fileSystem.GetReparsePointInfo(file1.Path);
        var info2 = _fileSystem.GetReparsePointInfo(file2.Path);
        return info1.Target == info2.Target;
      }

      // If same modification date, assume they are equal
      if (DateTime.Equals(file1.LastWriteTimeUtc, file2.LastWriteTimeUtc)) {
        return true;
      }

      using (var stream1 = _fileSystem.OpenFile(file1.Path, FileAccess.Read))
      using (var stream2 = _fileSystem.OpenFile(file2.Path, FileAccess.Read)) {
        return CompareStreamContents(stream1, stream2);
      }
    }

    private static bool CompareStreamContents(Stream stream1, Stream stream2) {
      var bytes1 = new byte[8192];
      var bytes2 = new byte[8192];
      while (true) {
        var count1 = stream1.Read(bytes1, 0, bytes1.Length);
        var count2 = stream2.Read(bytes2, 0, bytes2.Length);
        if (!CompareByteArrays(bytes1, count1, bytes2, count2)) {
          return false;
        }

        if (count1 == 0) {
          Debug.Assert(count2 == 0);
          return true;
        }
      }
    }

    private static bool CompareByteArrays(byte[] bytes1, int count1, byte[] bytes2, int count2) {
      if (count1 != count2) {
        return false;
      }

      for (var i = 0; i < count1; i++) {
        if (bytes1[i] != bytes2[i]) {
          return false;
        }
      }

      return true;
    }
  }
}