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

using System.Diagnostics.CodeAnalysis;

namespace mtsuite.CoreFileSystem.Win32 {
  /// <summary>
  /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms681382(v=vs.85).aspx
  /// </summary>
  [SuppressMessage("ReSharper", "InconsistentNaming")]
  public enum Win32Errors {
    ERROR_SUCCESS = 0,
    ERROR_INVALID_FUNCTION = 1,
    ERROR_FILE_NOT_FOUND = 2,
    ERROR_PATH_NOT_FOUND = 3,
    ERROR_ACCESS_DENIED = 5,
    ERROR_INVALID_DRIVE = 15,
    ERROR_NO_MORE_FILES = 18,
    ERROR_NOT_SUPPORTED = 50,
    ERROR_INVALID_PARAMETER = 87,
    ERROR_INSUFFICIENT_BUFFER = 122,
    ERROR_MORE_DATA = 234,
    ERROR_REQUEST_ABORTED = 1235,
    ERROR_PRIVILEGE_NOT_HELD = 1314,
  }

  public enum NtStatusErrors {
    STATUS_NO_MORE_FILES = unchecked((int)0x80000006),
    STATUS_INVALID_PARAMETER = unchecked((int)0xC000000D),
    STATUS_NOT_A_DIRECTORY = unchecked((int)0xC0000103),
  }
}
