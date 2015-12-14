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
using System.Diagnostics.CodeAnalysis;

namespace mtsuite.shared.Win32 {
  /// <summary>
  /// Note: Type of symlinks on Windows
  /// Directory
  ///   * Junction point (2000+): Reparse point to some other directory, full path only.
  ///   * Symbolic link (Vista+): Link to some other directory, including network paths. Absolute or relative.
  /// File
  ///   * Hard link
  ///   * Symbolic link (Vista+): Link to some other file, including network paths. Absolute or relative.
  /// See https://msdn.microsoft.com/en-us/library/windows/desktop/aa365682%28v=vs.85%29.aspx for
  /// behavior of Windows File IO API, specifically COPY_FILE_COPY_SYMLINK.
  /// </summary>
  [Flags]
  [SuppressMessage("ReSharper", "InconsistentNaming")]
  public enum FILE_ATTRIBUTE : uint {
    FILE_ATTRIBUTE_READONLY = 0x00000001,
    FILE_ATTRIBUTE_HIDDEN = 0x00000002,
    FILE_ATTRIBUTE_SYSTEM = 0x00000004,
    FILE_ATTRIBUTE_DIRECTORY = 0x00000010,
    FILE_ATTRIBUTE_ARCHIVE = 0x00000020,
    FILE_ATTRIBUTE_DEVICE = 0x00000040,
    FILE_ATTRIBUTE_NORMAL = 0x00000080,
    FILE_ATTRIBUTE_TEMPORARY = 0x00000100,
    FILE_ATTRIBUTE_SPARSE_FILE = 0x00000200,
    FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400,
    FILE_ATTRIBUTE_COMPRESSED = 0x00000800,
    FILE_ATTRIBUTE_OFFLINE = 0x00001000,
    FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x00002000,
    FILE_ATTRIBUTE_ENCRYPTED = 0x00004000,
  }
}