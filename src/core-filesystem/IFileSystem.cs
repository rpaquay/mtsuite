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
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtsuite.CoreFileSystem {
  
  /// <summary>
  /// A File system abstraction that contains a **minimal** set of methods required for
  /// all tools of "mtsuite" to work.
  /// </summary>
  public interface IFileSystem {
    /// <summary>
    /// Returns the <see cref="FileSystemEntry"/> corresponding to the <paramref
    /// name="path"/>. Throws an exception if the entry does not exist or is not
    /// accessible for some other reason.
    /// </summary>
    FileSystemEntry GetEntry(FullPath path);

    /// <summary>
    /// Get the <see cref="ReparsePointInfo"/> for the given <paramref
    /// name="path"/>. A <em>reparse point</em> can be a file or directory
    /// symbolic link, and directory junction point or any other kind of reparse
    /// point supported by NTFS. Throws an error if <paramref name="path"/> does
    /// not point to a reparse point or if the reparse point cannot be opened
    /// for some other reason.
    /// </summary>
    ReparsePointInfo GetReparsePointInfo(FullPath path);

    /// <summary>
    /// Returns the list of entries in a given directory <paramref
    /// name="path"/>. Each entry contains the relative name of the child file
    /// or directory, as well as the corresponding <see cref="FileSystemEntryData"/>.
    /// Throw an exception if <paramref name="path"/> does not exist or is not
    /// accessible for some reason.
    /// </summary>
    FromPool<List<FileSystemEntry>> GetDirectoryFiles(FullPath path, FullPathReference pathRef = default);

    /// <summary>
    /// Create a directory given its path. Throws if the directory already
    /// exists, or if there is another error condition preventing the directory
    /// creation.
    /// </summary>
    void CreateDirectory(FullPath path);

    /// <summary>
    /// Delete a file or directory, given the <see cref="FileSystemEntry"/> of
    /// the file/directory. Note that if <paramref name="entry"/> is a
    /// directory, it must be empty. Throws an exception if <paramref
    /// name="entry"/> does not exists or cannot be deleted for some reason.
    /// </summary>
    void DeleteEntry(FileSystemEntry entry);

    /// <summary>
    /// Copy a file (or reparse point or symbolic link) given its corresponding
    /// <see cref="FileSystemEntry"/> to <paramref name="destinationPath"/>.
    /// <paramref name="callback"/> is invoked at regular invervals when the
    /// copy operation takes more than a few milliseconds.
    /// 
    /// Note: Throws an exception <paramref name="sourceEntry"/> does not
    /// exists, or is not accessible for some reason.
    /// 
    /// Note: Throws an exception when the destination can't be overwritten
    /// successfully.
    /// </summary>
    void CopyFile<T>(FileSystemEntry sourceEntry, FullPath destinationPath, CopyFileOptions options, T param, CopyFileCallback<T> callback);

    /// <summary>
    /// Copy a file (or reparse point or symbolic link) given its corresponding
    /// <see cref="FileSystemEntry"/> to <paramref name="destinationEntry"/>.
    /// <paramref name="callback"/> is invoked at regular invervals when the
    /// copy operation takes more than a few milliseconds.
    /// 
    /// Note: This is a slightly more efficient version of the other overload
    /// because this overload knows more about the state of the destination
    /// path.
    /// 
    /// Note: Throws an exception <paramref name="sourceEntry"/> does not
    /// exists, or is not accessible for some reason.
    /// 
    /// Note: Throws an exception when the destination can't be overwritten
    /// successfully.
    /// </summary>
    void CopyFile<T>(FileSystemEntry sourceEntry, FileSystemEntry destinationEntry, CopyFileOptions options, T param, CopyFileCallback<T> callback);
    
    /// <summary>
    /// Open an existing file given its <paramref name="path"/>. Throws an
    /// exception if the file does not exist or cannot be opened with the
    /// requested access for some reason.
    /// </summary>
    FileStream OpenFile(FullPath path, FileAccess access);
  }
}
