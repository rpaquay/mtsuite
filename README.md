# mtsuite

**mtsuite** is a collection of high-performance multi-threaded command-line disk utilities for macOS, Linux, and Windows, optimized for Solid State Drives (SSDs).

Each utility in the suite:

* **Leverages all available CPU cores** for maximum disk throughput and concurrent I/O.
* **Displays real-time worker thread progress** and live status updates during operation.
* **Supports native file cloning**:
  * macOS APFS (`clonefile`)
  * Windows ReFS / Dev Drive (`FSCTL_DUPLICATE_EXTENTS_TO_FILE`)
  for instant zero-copy file duplication and deduplication in `mtcopy`, `mtmir`, and `mtcompact`.
* **Preserves Symbolic Links and Junction Points** (Junction Points are a Windows-only feature), copying and deleting links as link objects rather than following target content.
* **Supports long paths** (> 260 characters).

---

## Included Utilities

* **`mtcopy`**: Recursively copies a source directory to a destination directory in parallel. Similar to `ROBOCOPY /S`, `xcopy /S`, or `rsync -r`, with automatic file cloning support on macOS (APFS) and Windows (ReFS / Dev Drive).
* **`mtmir`**: Mirrors a source directory to a destination directory, copying new/modified files and deleting extra destination files not present in the source. Similar to `ROBOCOPY /MIR` or `rsync -a --delete`. Supports native cloning on macOS (APFS) and Windows (ReFS / Dev Drive).
* **`mtdel`**: Recursively deletes a directory tree in parallel. Significantly faster than `rm -rf` or `rmdir /s /q`.
* **`mtinfo`**: Recursively examines a directory tree and displays comprehensive statistics (file counts, directory counts, total size, symlink counts, and depth summaries).
* **`mtfind`**: Recursively searches for files and directories matching file name wildcard patterns (similar to `find`), displaying matching paths in real-time alongside live progress tracking.
* **`mtfindstr`**: Recursively searches inside file contents for text strings in parallel (similar to `grep` or `findstr`), displaying matching line and column hits in real-time.
* **`mtcompact`**: Recursively compares directory entries and compacts/deduplicates identical files using file cloning (turning duplicate file content into copy-on-write file clones on APFS and ReFS to reclaim disk space).

---

## Symbolic Links & Junction Points Support

A Symbolic Link (or Junction Point on Windows) points to another file or directory on disk. 

Unlike traditional tools (`XCOPY`, `ROBOCOPY`, `rsync`) that default to expanding link targets into full duplicate files unless special flags are provided, **mtsuite** tools (`mtcopy`, `mtmir`, `mtdel`) preserve link semantics:
- **`mtcopy` / `mtmir`** replicate symbolic links and Windows Junction Points as links rather than copying the target contents.
- **`mtdel`** safely unlinks directory/file symbolic links and Windows Junction Points without accidentally resolving or recursively deleting into target directories.

### Example

Given the following folder structure:

```
c:\test
├── foo
│   └── bar.rlink.txt (symbolic link -> "..\bar.txt")
├── foo2
│   └── foo.link (symbolic link -> "..\foo")
└── bar.txt (file)
```

Running `mtcopy c:\test c:\test-copy` results in:

```
c:\test-copy
├── foo
│   └── bar.rlink.txt (symbolic link -> "..\bar.txt")
├── foo2
│   └── foo.link (symbolic link -> "..\foo")
└── bar.txt (file)
```

The symbolic links (and Junction Points on Windows) are preserved as relative links in the target directory rather than expanding their target content.
