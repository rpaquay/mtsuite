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

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace mtsuite.CoreFileSystem.Win32 {
  public class LastWin32ErrorException : Win32Exception {
    public LastWin32ErrorException()
      : base(Marshal.GetLastWin32Error()) {
    }
    public LastWin32ErrorException(string message)
      : this(Marshal.GetLastWin32Error(), message) {
    }
    public LastWin32ErrorException(int errorCode)
      : base(errorCode) {
    }
    public LastWin32ErrorException(int errorCode, string message)
      : base(errorCode, string.Format("{0}: {1}", message, new Win32Exception(errorCode).Message)) {
    }
  }
}