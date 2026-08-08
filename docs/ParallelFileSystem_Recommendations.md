# Recommendations for ParallelFileSystem

This document analyzes [`ParallelFileSystem.cs`](file:///usr/local/google/home/rpaquay/src/mtsuite/src/shared/ParallelFileSystem.cs) and outlines concrete recommendations to improve **Performance**, **Correctness & Reliability**, and **Cross-OS Portability**.

---

## 1. Performance Recommendations

### 1.1 Parallelize File Copies Within Each Directory (Critical Bottleneck)
* **Current Implementation:**
  In [`CopyDirectoryEntriesAsync`](file:///usr/local/google/home/rpaquay/src/mtsuite/src/shared/ParallelFileSystem.cs#L209-L223), subdirectory tasks are created in parallel, but inside each directory, all files are copied sequentially on a single thread:
  ```csharp
  private void CopyFileEntries(
    List<FileSystemEntry> sourceEntries,
    FileSystemEntry destinationDirectory,
    IFileComparer fileComparer,
    SmallSet<FileSystemEntry> destinationSet) {

    var sw = new Stopwatch();
    foreach (var entry in sourceEntries) {
      CopyFileEntry(sw, entry, destinationDirectory, fileComparer, destinationSet);
    }
  }
  ```
* **Problem:** In directories containing thousands of files in a flat structure (e.g. photos, logs, datasets, package caches), **parallelism collapses to a single core**, leaving all other CPU cores idle.
* **Proposed Fix:** Dispatch file copies concurrently via `_taskFactory` (either individually or in small batches of 16–64 files):
  ```csharp
  var fileTasks = _taskFactory.CreateCollection(sourceEntries
    .Where(entry => entry.IsFile || entry.IsReparsePoint)
    .Select(sourceEntry => _taskFactory.StartNew(() => {
      CopyFileEntry(new Stopwatch(), sourceEntry, destinationDirectory, fileComparer, destinationSet);
    })));

  return fileTasks.ContinueWith(_ => {
    // Complete directory
  });
  ```

---

### 1.2 Eliminate Quadratic $O(N \times M)$ Loop in `ComputeDestinationEntriesToDelete`
* **Current Implementation:**
  ```csharp
  // TODO: Perf: Need a hashset?
  var mismatchedEntries = destinationEntries
    .Where(dst => {
      foreach (var src in sourceEntries) {
        if (PathHelpers.FileNameComparer.Equals(dst.Name, src.Name)) {
          if (dst.IsFile != src.IsFile ||
              dst.IsDirectory != src.IsDirectory ||
              dst.IsReparsePoint != src.IsReparsePoint) {
            return true;
          }
        }
      }
      return false;
    });
  ```
* **Problem:** In a directory with 10,000 files, this nested loop executes **100,000,000 string comparisons**.
* **Proposed Fix:** Build a fast name lookup (or use a dictionary/set) to achieve $O(1)$ lookups, reducing total complexity from **$O(N \times M)$ down to $O(N)$**:
  ```csharp
  var sourceDict = new Dictionary<string, FileSystemEntry>(sourceEntries.Count, PathHelpers.FileNameComparer);
  foreach (var src in sourceEntries) {
    sourceDict.TryAdd(src.Name, src);
  }

  foreach (var dst in destinationEntries) {
    if (sourceDict.TryGetValue(dst.Name, out var src)) {
      if (dst.IsFile != src.IsFile || dst.IsDirectory != src.IsDirectory || dst.IsReparsePoint != src.IsReparsePoint) {
        entriesToDelete.Add(dst);
      }
    }
  }
  ```

---

### 1.3 Eliminate Delegate & Closure Allocations in `SmallSet<T>`
* **Current Implementation:**
  In [`SmallSet.cs`](file:///usr/local/google/home/rpaquay/src/shared/Collections/SmallSet.cs#L55-L60), calling `SetList` assigns new lambda closures (`_contains = x => ...`, `_tryGet = ...`) on **every single directory traversal**, defeating the purpose of object pooling.
* **Proposed Fix:** Replace delegate fields in `SmallSet<T>` with direct instance methods:
  ```csharp
  public bool Contains(T item) {
    if (_itemsDic != null) return _itemsDic.ContainsKey(item);
    if (_itemsList != null) {
      for (int i = 0; i < _itemsList.Count; i++) {
        if (_comparer.Equals(item, _itemsList[i])) return true;
      }
    }
    return false;
  }
  ```

---

### 1.4 Allow Custom / Throttled `ITaskFactory` Injection
* **Current Implementation:**
  ```csharp
  private readonly ITaskFactory _taskFactory = new DefaultTaskFactory();
  ```
* **Problem:** `ParallelFileSystem` hardcodes `DefaultTaskFactory` and does not accept an `ITaskFactory` in its constructor, preventing callers from enforcing concurrency limits (e.g. `--threads:count`).
* **Proposed Fix:** Add an optional `ITaskFactory taskFactory = null` constructor parameter:
  ```csharp
  public ParallelFileSystem(IFileSystem fileSystem, ITaskFactory taskFactory = null) {
    _fileSystem = fileSystem;
    _taskFactory = taskFactory ?? new DefaultTaskFactory();
  }
  ```

---

## 2. Correctness & Reliability Recommendations

### 2.1 Prevent Accidental Destination Wiping on Source Read Errors
* **Current Implementation:**
  In [`GetDirectoryEntries`](file:///usr/local/google/home/rpaquay/src/mtsuite/src/shared/ParallelFileSystem.cs#L56-L64):
  ```csharp
  private FromPool<List<FileSystemEntry>> GetDirectoryEntries(FullPath directoryPath) {
    try {
      return _fileSystem.GetDirectoryFiles(directoryPath);
    } catch (Exception e) {
      OnError(directoryPath, e);
      // Assume no entries available on error, so we can continue processing
      return _entryListPool.AllocateFrom();
    }
  }
  ```
* **Critical Risk in `mtmir`:** If a source directory fails to read due to a temporary permissions or access error, returning an empty list causes `DeleteExtraFiles` in `mtmir` to assume the source directory is empty and **delete all files in the destination directory**!
* **Proposed Fix:** Distinguish successful empty directories from failed directory reads so that destination deletion (`ComputeDestinationEntriesToDelete`) is skipped if the source could not be read:
  ```csharp
  private bool TryGetDirectoryEntries(FullPath directoryPath, out FromPool<List<FileSystemEntry>> entries) {
    try {
      entries = _fileSystem.GetDirectoryFiles(directoryPath);
      return true;
    } catch (Exception e) {
      OnError(directoryPath, e);
      entries = _entryListPool.AllocateFrom();
      return false;
    }
  }
  ```

---

### 2.2 Guaranteed Object Pool Disposal
* **Current Implementation:**
  In `CopyDirectoryEntriesAsync`, `sourceEntries`, `destinationEntries`, and `destinationSet` are allocated at the start, and `.Dispose()` is called inside a `.ContinueWith(...)` continuation.
* **Risk:** If an exception occurs before the task is scheduled, or if task creation fails, the pooled lists are never returned to the pool, leaking pool capacity.
* **Proposed Fix:** Wrap allocation in a structured `try/catch` block that releases resources on failure.

---

### 2.3 Thread-Safe Timestamp Tracking
* **Current Implementation:**
  `CopyFileEntries` instantiates a single `Stopwatch sw` and passes it across invocations. If file copies run concurrently across threads, sharing a single `Stopwatch` will corrupt timing measurements.
* **Proposed Fix:** Use separate `Stopwatch` instances per worker task or use .NET's static zero-allocation `Stopwatch.GetTimestamp()`.

---

## 3. Cross-OS Portability Recommendations

| Category | Windows | Linux | macOS (Darwin) | Recommendation |
| :--- | :--- | :--- | :--- | :--- |
| **Case-Sensitivity Clashes** | Case-preserving, case-insensitive | Case-sensitive (ext4/btrfs) | Case-preserving, case-insensitive (APFS) | When populating dictionaries in `SmallSet` and `ComputeDestinationEntriesToDelete`, use `TryAdd` rather than `Add` to prevent duplicate key crashes when mirroring between case-sensitive and case-insensitive environments. |
| **Directory Reparse Points / Symlinks** | Windows Junctions & Symlinks | POSIX directory symlinks | POSIX directory symlinks | Ensure `DeleteEntryAsync` only unlinks directory symlinks without recursively deleting the link's target contents. |
| **Thread Count Control** | Global `ThreadPool` | Global `ThreadPool` | Global `ThreadPool` | Allow `ITaskFactory` injection to respect `--threads:count` uniformly. |

---

## 4. Summary Table of Recommendations

| Item | Area | Description | Expected Benefit |
| :--- | :--- | :--- | :--- |
| **1.1** | **Performance** | Intra-directory parallel file copying | $5\times - 20\times$ faster copy for flat directories |
| **1.2** | **Performance** | $O(N)$ index lookup for mismatched file deletion | Eliminates $O(N \times M)$ bottleneck on large dirs |
| **1.3** | **Performance** | Remove delegate allocations in `SmallSet<T>` | Eliminates GC Gen 0 churn during directory walking |
| **1.4** | **Performance** | Allow `ITaskFactory` injection in constructor | Enables thread throttling (`--threads:count`) |
| **2.1** | **Correctness** | Guard against source read errors in `mtmir` | Prevents accidental deletion of destination data |
| **2.2** | **Correctness** | Safe pool cleanup on task exception | Prevents object pool leaks |
| **2.3** | **Correctness** | Per-thread stopwatch / timestamping | Thread-safe progress and duration reporting |
| **3.1** | **Portability** | `TryAdd` in set/dictionary indexing | Robustness against case-sensitivity collisions |
