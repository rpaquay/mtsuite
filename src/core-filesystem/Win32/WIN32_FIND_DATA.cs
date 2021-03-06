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
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;

namespace mtsuite.CoreFileSystem.Win32 {
  [BestFitMapping(false)]
  [Serializable]
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
  [SuppressMessage("ReSharper", "InconsistentNaming")]
  public struct WIN32_FIND_DATA {
    internal uint dwFileAttributes;
    internal uint ftCreationTime_dwLowDateTime;
    internal uint ftCreationTime_dwHighDateTime;
    internal uint ftLastAccessTime_dwLowDateTime;
    internal uint ftLastAccessTime_dwHighDateTime;
    internal uint ftLastWriteTime_dwLowDateTime;
    internal uint ftLastWriteTime_dwHighDateTime;
    internal uint nFileSizeHigh;
    internal uint nFileSizeLow;
    internal uint dwReserved0;
    internal uint dwReserved1;
    internal unsafe fixed char cFileName[260];
    internal unsafe fixed char cAlternateFileName[14];

    /// <summary>
    /// Returns the <see cref="cFileName"/> field as a string. This implies a memory allocation.
    /// </summary>
    [Pure]
    public string GetFileName() {
      unsafe {
        fixed (char* name = cFileName) {
          return new string(name);
        }
      }
    }

    /// <summary>
    /// Returns the <see cref="cAlternateFileName"/> field as a string. This implies a memory allocation.
    /// </summary>
    [Pure]
    public string GetAlternativeFileName() {
      unsafe {
        fixed (char* name = cFileName) {
          return new string(name);
        }
      }
    }
  }
}