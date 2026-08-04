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

using System.IO;

namespace mtsuite.CoreFileSystem;

public interface IFileSystemExtended : IFileSystem {

    /// <summary>
    /// Gets the <see cref="mtsuite.CoreFileSystem.ObjectPool.INameTable"/> instance used by this file system for interning file names.
    /// </summary>
    mtsuite.CoreFileSystem.ObjectPool.INameTable NameTable { get; }

    /// <summary>
    /// Get the <see cref="FileSystemEntry"/> corresponding to <paramref
    /// name="path"/>. Return false if the entry does not exist or is not
    /// accessible for some other reason.
    /// </summary>
    bool TryGetEntry(FullPath path, out FileSystemEntry entry);

    /// <summary>
    /// Returns the list of entries in a given directory <paramref
    /// name="path"/>. Each entry contains the relative name of the child file
    /// or directory, as well as the corresponding <see cref="FILE_ATTRIBUTE"/>.
    /// Throw an exception if <paramref name="path"/> does not exist or is not
    /// accessible for some reason.
    /// </summary>
    //FromPool<List<FileSystemEntry>> GetDirectoryEntries(FullPath path, string pattern = null);

    //DirectoryEntriesEnumerator<FullPath> GetDirectoryEntriesEnumerator(FullPath path, string pattern = null);

    /// <summary>
    /// Enumerates the entries in a given directory <paramref
    /// name="path"/>. Each entry contains the relative name of the child file
    /// or directory, as well as the corresponding <see cref="FILE_ATTRIBUTE"/>.
    /// Throw an exception if <paramref name="path"/> does not exist or is not
    /// accessible for some reason.
    /// 
    /// Note: A handle is held on the underlying directory until the enumeration is disposed.
    /// </summary>
    //IEnumerable<FileSystemEntry> EnumerateDirectoryEntries(FullPath path, string pattern = null);


    //DirectoryFilesEnumerator<FullPath> GetDirectoryFilesEnumerator(FullPath path, string pattern = null);

    //IEnumerable<FileSystemEntry> EnumerateDirectoryFiles(FullPath path, string pattern = null);


    /// <summary>
    /// Create a new file given its <paramref name="path"/>. Throws an exception
    /// if <paramref name="path"/> already exists or if the file can't be
    /// createdfor some reason.
    /// </summary>
    FileStream CreateFile(FullPath path);

    /// <summary>
    /// Create a file symbolic link given its path and <paramref name="target"/>
    /// -- a relative or absolute path. Symblic link are only supported on
    /// Windows Vista and later. Creating symbolic links requires the <a
    /// href="http://superuser.com/questions/124679/how-do-i-create-a-link-in-windows-7-home-premium-as-a-regular-user/125981#125981">SeCreateSymbolicLinkPrivilege</a>.
    /// Throws if the directory already exists, or if there is another error
    /// condition preventing the link creation.
    /// </summary>
    void CreateFileSymbolicLink(FullPath path, string target);

    /// <summary>
    /// Create a directory symbolic link given its path and <paramref
    /// name="target"/> -- a relative or absolute path. Symblic links are only
    /// supported on Windows Vista and later. Creating symbolic links requires
    /// the <a
    /// href="http://superuser.com/questions/124679/how-do-i-create-a-link-in-windows-7-home-premium-as-a-regular-user/125981#125981">SeCreateSymbolicLinkPrivilege</a>.
    /// Throws if the directory already exists, or if there is another error
    /// condition preventing the link creation.
    /// </summary>
    void CreateDirectorySymbolicLink(FullPath path, string target);

    /// <summary>
    /// Create a junction point given its path and <paramref name="target"/> --
    /// a relative or absolute path.
    /// 
    /// Note that if <paramref name="target"/> is relative, the file system
    /// will expand it to an absolute path, as the underlying file systems
    /// only support absolute paths for junction point targets.
    /// 
    /// Throws if <paramref name="path"/> already exists, or if there is another
    /// error condition preventing the junction point creation.
    /// </summary>
    void CreateJunctionPoint(FullPath path, string target);
}