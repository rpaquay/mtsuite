﻿// Copyright 2015 Renaud Paquay All Rights Reserved.
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
using System.IO;

namespace mtsuite.CoreFileSystem.Win32 {
  /// <summary>
  /// File or directory entry information returned by <see
  /// cref="Win32{TPath}.GetDirectoryEntries"/>, including the <see cref="FileName"/>
  /// and file attributes.
  /// </summary>
  public struct DirectoryEntry {
    internal WIN32_FIND_DATA Data;

    public DirectoryEntry(WIN32_FIND_DATA data) {
      Data = data;
    }

    public string FileName {
      get { return Data.GetFileName(); }
    }

    public Int64 Length => HighLowToLong(Data.nFileSizeHigh, Data.nFileSizeLow);

    public DateTime CreationTimeUtc => HighLowToDateTimeUtc(Data.ftCreationTime_dwHighDateTime, Data.ftCreationTime_dwLowDateTime);

    public DateTime LastWriteTimeUtc => HighLowToDateTimeUtc(Data.ftLastWriteTime_dwHighDateTime, Data.ftLastWriteTime_dwLowDateTime);

    public DateTime LastAccessTimeUtc => HighLowToDateTimeUtc(Data.ftLastAccessTime_dwHighDateTime, Data.ftLastAccessTime_dwLowDateTime);

    public FileAttributes Attributes => (FileAttributes)Data.dwFileAttributes;

    private DateTime HighLowToDateTimeUtc(uint high, uint low) {
      return DateTime.FromFileTimeUtc(HighLowToLong(high, low));
    }

    private static long HighLowToLong(uint high, uint low) {
      return low + ((long)high << 32);
    }
  }
}