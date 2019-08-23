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
using System.Collections;
using System.Collections.Generic;
using Microsoft.Win32.SafeHandles;

namespace mtsuite.CoreFileSystem.Win32 {
  /// <summary>
  /// Enumerator for the list of entries of a directory.  Implements of 
  /// <see cref="IEnumerator{DirectoryEntry}"/> as a high-performance
  /// value type. No heap allocation is performeed, except for a <see cref="SafeFindHandle"/>
  /// 
  /// <para>
  /// Note: This is a mutable value type, so copying the value and re-using it
  /// in different context may lead to unspecified behavior. The intent is to use this
  /// value type as local variables used in simple <see cref="MoveNext"/>-<see cref="CurrentEntry"/>
  /// loops.
  /// </para>
  /// </summary>
  public struct DirectoryFilesEnumerator<TPath> : IEnumerator<DirectoryEntry> {
    private readonly Win32<TPath> _win32;
    private readonly TPath _directoryPath;
    private readonly SafeFileHandle _fileHandle;
    private readonly SafeHGlobalHandle _bufferHandle;
    private int _bufferOffset;
    private bool _reachedEOF;

    public DirectoryFilesEnumerator(Win32<TPath> win32, TPath directoryPath, string pattern) {
      CurrentEntry = default(DirectoryEntry);
      _win32 = win32;
      _directoryPath = directoryPath;
      _bufferOffset = 0;
      _reachedEOF = false;
      _bufferHandle = new SafeHGlobalHandle(8_192);
      // FILE_LIST_DIRECTORY to notify we will read the entries of the direction (file)
      // BackupSemantics is required to allow reading entries
      _fileHandle = _win32.OpenFile(_directoryPath,
        NativeMethods.EFileAccess.FILE_LIST_DIRECTORY,
        NativeMethods.EFileShare.Read | NativeMethods.EFileShare.Write | NativeMethods.EFileShare.Delete,
        NativeMethods.ECreationDisposition.OpenExisting,
        NativeMethods.EFileAttributes.BackupSemantics);

      _reachedEOF = !_win32.InvokeNtQueryDirectoryFile(directoryPath, _fileHandle, _bufferHandle, true, pattern);
    }

    /// <summary>
    /// The current entry, exposed without any copying
    /// </summary>
    public DirectoryEntry CurrentEntry;

    public DirectoryEntry Current {
      get {
        return CurrentEntry;
      }
    }

    public void Dispose() {
      _fileHandle?.Close();
    }

    public bool MoveNext() {
      if (_fileHandle == null || _fileHandle.IsClosed || _reachedEOF) {
        return false;
      }

      while (true) {
        // Fetch next set of entries if needed
        if (_bufferOffset < 0) {
          _reachedEOF = !_win32.InvokeNtQueryDirectoryFile(_directoryPath, _fileHandle, _bufferHandle, false, null);
          if (_reachedEOF) {
            // Close eagerly to release resources as fast as possible
            _fileHandle.Close();
            return false;
          }
          _bufferOffset = 0;
        }

        // Extract entry at "_bufferOffset"
        _bufferOffset = _win32.ExtractQueryDirectoryFileEntry(_bufferHandle, _bufferOffset, out CurrentEntry.Data);

        // Skip "." and ".."
        if (!Win32<TPath>.SkipSpecialEntry(ref CurrentEntry.Data)) {
          return true;
        }
      }
    }

    public void Reset() {
      throw new NotImplementedException();
    }

    object IEnumerator.Current {
      get { return Current; }
    }
  }
}
