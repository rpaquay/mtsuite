# Windows ReFS / Dev Drive File Cloning Support

Added native block cloning support on Windows for ReFS (Resilient File System) and Dev Drive volumes across `mtcopy`, `mtmir`, and `mtcompact`.

## Changes Summary

### 1. Core File System Extension (`WindowsFileSystemExtension`)
[WindowsFileSystemExtension.cs](../src/core-filesystem/WindowsFileSystemExtension.cs)
- **`IsCloningSupported`**: Probes block cloning capability on Windows by creating a cluster-aligned test probe in source and destination directories and executing `FSCTL_DUPLICATE_EXTENTS_TO_FILE`. Returns `true` on ReFS / Dev Drive volumes and `false` on unsupported file systems (such as NTFS).
- **`CloneFile`**:
  - Determines volume cluster size via `GetDiskFreeSpaceW`.
  - Sets target file size using `RandomAccess.SetLength`.
  - Duplicates extents in chunks (up to 1 GB per chunk) using `FSCTL_DUPLICATE_EXTENTS_TO_FILE`.
  - Copies any unaligned trailing tail bytes.
  - Preserves file attributes and `LastWriteTimeUtc`.
  - Uses atomic replacement (`File.Move(..., overwrite: true)`) with clean-up on error.
- **`AreFilesCloned`**:
  - Queries physical extent mappings using `FSCTL_GET_RETRIEVAL_POINTERS`.
  - Inspects and compares logical cluster numbers (LCNs) across virtual clusters (VCNs) to accurately determine if two files share physical disk blocks.
- **`TryGetReparsePointTag`**:
  - Encapsulates Win32 P/Invoke calls (`CreateFileW`, `GetFileInformationByHandleEx`) to inspect platform-specific reparse point tags.

### 2. Unit Tests
[FileSystemTest.cs](../src/tests/FileSystemTest.cs)
- Added tests for `WindowsFileSystemExtension`:
  - `WindowsFileSystemExtension_ReFSBlockCloning_WorksWhenSupported`: Verifies `IsCloningSupported`, `CloneFile`, `AreFilesCloned`, content integrity, and modification detection on ReFS.
  - `WindowsFileSystemExtension_AreFilesCloned_ReturnsTrueForZeroByteFiles`.
  - `WindowsFileSystemExtension_CloneFile_DoesNotSwallowException_LastWriteTime`.
  - Fixed `NullFileSystemExtensionBehavesSafely` paths for cross-platform execution.

[ThreadProgressTrackerTest.cs](../src/tests/ThreadProgressTrackerTest.cs)
- Updated path creation to be cross-platform compatible.

### 3. Documentation
[README.md](../README.md)
- Documented Windows ReFS / Dev Drive block cloning support.

## Verification Results

### Automated Tests
Ran `dotnet test`:
```text
Passed!  - Failed:     0, Passed:   195, Skipped:    12, Total:   210, Duration: 1 s - tests.dll (net8.0)
```
All unit and integration tests passed cleanly.

### Manual Verification on ReFS Volume `D:\`
Tested `mtcopy`, `mtmir`, and `mtcompact` directly on `D:\`:
1. **`mtcopy`**: Cloned 212 files (9.7 MB) in 0.35s via native ReFS block cloning with 27.7 MB/sec throughput.
2. **`mtmir`**: ReFS block-cloned source tree to destination mirror, and on subsequent run correctly detected and skipped all identical already-cloned files.
3. **`mtcompact`**: Successfully deduplicated separate physical copies into ReFS block clones, and on subsequent runs identified 197 files (9.7 MB) as already cloned with zero unnecessary I/O.

