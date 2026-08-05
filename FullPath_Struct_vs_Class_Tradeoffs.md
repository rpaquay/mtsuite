# FullPath: Struct vs. Class Architectural Tradeoffs

## Executive Summary

`FullPath` is the core path abstraction in `mtsuite`, representing file system paths hierarchically as a linked tree of segments (parent path + leaf name). 

Previously, `FullPath` was implemented as a `struct` to minimize managed heap allocations. However, because C# value types cannot recursively contain themselves without causing infinite layout cycles, it required an unmanaged-style static chunked array arena (`FullPathReferenceNoRelease`) and an index wrapper struct (`FullPathReference`).

Migrating `FullPath` from a `struct` to a `sealed class` removes this entire arena subsystem, replaces indirect chunk lookups with direct reference pointers, and relies on the .NET Garbage Collector for lifecycle management.

---

## Architecture Comparison

```
PREVIOUS (Struct + Static Arena):
+-------------------------+
| FullPath (struct)       |
|  - _parent: int (index) | ---> [FullPathReferenceNoRelease.s_chunks[][]] (Unbounded Heap Arena)
|  - _name: string        |
+-------------------------+

CURRENT (Reference Type / Class):
+-------------------------+
| FullPath (class)        |
|  - _parent: FullPath?   | ---> Direct object reference (Managed GC Heap)
|  - _name: string        |
+-------------------------+
```

---

## Detailed Tradeoff Analysis

### 1. Memory Layout and Footprint

| Dimension | `struct` Implementation | `class` Implementation | Tradeoff / Analysis |
| :--- | :--- | :--- | :--- |
| **Instance Size** | 16 bytes (4-byte index, 8-byte string ref, 4-byte padding) | 32 bytes on 64-bit (16-byte object header + MT pointer, 8-byte parent ref, 8-byte string ref) | **Class uses more heap memory per path node.** |
| **Stack / Parameter Passing** | 16 bytes copied by value on call stack / registers | 8-byte pointer passed by value | **Class is faster and lighter to pass across methods.** |
| **Parent Storage** | Copied into 4,096-element chunked arrays (`FullPath[][]`) in `FullPathReferenceNoRelease` | Direct pointer to parent `FullPath` object on managed heap | **Class eliminates arena chunk management overhead.** |
| **Memory Lifetime** | Static / permanent (Arena memory is never released for process lifetime) | Ephemeral / managed (GC collects unreferenced path instances) | **Class prevents memory leaks in long-running processes.** |

---

### 2. Execution Performance and CPU Efficiency

#### Advantages of `class`
- **Zero Indirection on Parent Traversal:**
  - *Struct*: Navigating up the tree (`.Parent`, `.FullName`, `.Length`, `.ComparePaths`) required calculating chunk index and item offset: `s_chunks[index >> 12][index & 0xFFF]`.
  - *Class*: Direct field dereference (`_parent`), benefiting from CPU L1/L2 data caching and branch prediction.
- **Fast Lock-Free Allocation:**
  - *Struct*: Creating a parent reference called `FullPathReferenceNoRelease.Allocate()`, which executed `Interlocked.Increment` on a global atomic counter, bounds checks, and volatile array reads.
  - *Class*: Standard managed allocation `new FullPath(parent, name)` directly in thread-local allocation context (TLAC) with zero atomic cross-thread synchronization.
- **Pointer Reference Identity Optimization:**
  - Checking `ReferenceEquals(x, y)` or `ReferenceEquals(x._parent, y._parent)` provides an instant $O(1)$ fast path for paths sharing identical instances or parent directories.
- **Passing Efficiency in Aggregates:**
  - `FileSystemEntry` is a struct containing `FullPath`. Storing an 8-byte object reference is smaller than storing a 16-byte `FullPath` struct.

#### Advantages of `struct`
- **Zero GC Pressure:**
  - Paths stored in the arena never triggered Gen 0, Gen 1, or Gen 2 garbage collections.
- **Cache Locality for Arena Iterations:**
  - Chunks of 4,096 contiguous `FullPath` structs offered high locality when accessed sequentially.

---

### 3. Garbage Collection Impact

- **Object Sharing Mitigation:**
  - When scanning a directory containing $N$ files, all $N$ child `FullPath` instances share a single reference to the directory's `FullPath` instance. Only 1 parent object is shared among all siblings.
- **Generational GC Alignment:**
  - Directory traversals (e.g. in `mtfind`, `mtcopy`, `mtdel`) create transient paths that are processed and immediately discarded. Modern .NET 8 GC collects short-lived Gen 0 objects in sub-millisecond pauses.
- **Elimination of Static Arena Growth:**
  - In very large filesystem operations (e.g. 10+ million entries), `FullPathReferenceNoRelease` could hold hundreds of megabytes of permanently uncollectable arrays in memory. With a class, memory is released as soon as batches or directory branches complete.

---

### 4. API Ergonomics and Code Cleanliness

1. **Elimination of Secondary Abstractions:**
   - Deleted `FullPathReference` and `FullPathReferenceNoRelease`.
   - Removed awkward `FullPathReference pathRef = default` parameters from `IFileSystem.GetDirectoryFiles()`, `FileSystemPortable`, and `ParallelFileSystem`.
2. **Idiomatic Nullability:**
   - Uses standard C# nullable reference types (`FullPath?`) where `null` naturally denotes root paths or absence of paths, rather than checking `_parent.IsNull` or `_index == 0`.
3. **Operator Overloads:**
   - Consistent `==` and `!=` operator behavior adhering to standard value-object class patterns in C#.

---

## Summary Comparison Matrix

| Attribute | `FullPath` as `struct` (Arena) | `FullPath` as `class` (Heap) |
| :--- | :--- | :--- |
| **Object Allocation** | None on managed heap (allocated in custom arena) | Gen 0 managed heap object |
| **Memory Reclamation** | ❌ Never freed during process lifetime | ✅ Automatically reclaimed by GC |
| **Parent Access Latency** | 2-level array indexing (`chunks[i >> 12][i & mask]`) | Direct pointer dereference (`_parent`) |
| **Creation Thread Synchronization** | `Interlocked.Increment` on global counter | Zero synchronization (Thread-Local Alloc Context) |
| **Parameter Size** | 16 bytes | 8 bytes |
| **API Complexity** | High (`FullPath` + `FullPathReference` + Arena) | Low (`FullPath` only) |

---

## Empirical Benchmark Results

### Workload
- **Tool**: `MTINFO` (Multi-Threaded Directory Information)
- **Target**: `~/src/studio`
- **Volume**: **1,117,633 directories**, **4,621,711 files** (854.5 GB) — **~5.74 million total entries**

### Benchmark Measurements

| Metric | Before (`struct` + Arena) | After (`class`) | Delta | Impact |
| :--- | :--- | :--- | :--- | :--- |
| **Elapsed Time (Wall-Clock)** | **5.51s** | **5.33s** | **-0.18s (-3.3%)** | 🚀 **Faster** |
| **Throughput (entries/sec)** | **838,956** | **866,774** | **+27,818 (+3.3%)** | 🚀 **Higher** |
| **Final Retained Memory** | 94,372 KB (+ 17,472 KB arena) | 63,136 KB | **-48,708 KB (-43.6%)** | 🟢 **Substantially lower** |
| **Total Memory Allocated** | 2,018.93 MB | 2,067.27 MB | **+48.34 MB (+2.4%)** | 🟡 **Negligible increase** |
| **GC Pause Duration** | 1,713.29 ms (30.21%) | 1,461.62 ms (26.78%) | **-251.67 ms (-14.7%)** | 🟢 **15% less pause time** |
| **GC Collections (Gen0 / Gen1 / Gen2)** | 82 / 40 / 7 | 85 / 39 / 10 | +3 Gen0, -1 Gen1, +3 Gen2 | 🟡 **Minor shift** |
| **CPU Time (Aggregate All Cores)** | 02m 31.30s (151.3s) | 02m 54.09s (174.1s) | **+22.8s (+15.1%)** | 🔴 **Higher CPU core usage** |

---

## Conclusions & Analysis

1. **Wall-Clock Throughput Improved (+3.3%):**
   Direct field dereferencing (`_parent`), 8-byte pointer passing, and the removal of atomic contention (`Interlocked.Increment` in `Allocate()`) allowed parallel threads to complete directory traversals faster.

2. **Negligible Allocation Overhead (+2.4%):**
   Across 5.74 million filesystem entries, total allocations increased by only **48.3 MB** (~8.4 bytes per filesystem entry). Because all child entries in each folder share the single parent `FullPath` reference, heap allocation was far lower than theoretical worst-case estimates.

3. **GC Pause Times Decreased by 14.7% (1.71s → 1.46s):**
   Counter-intuitively, GC pauses were shorter with `class`. The old `struct` arena pinned 1.12 million parent items in permanent Gen 2 memory, forcing the GC mark phase to traverse a large live root set. With the `class` approach, ephemeral child paths die quickly in Gen 0/1, resulting in cleaner and faster generational sweeps.

4. **Retained Memory Footprint Dropped by 43.6%:**
   Final process memory dropped from **~112 MB** (94 MB heap + 17.5 MB static arena) down to **63 MB**. Memory is naturally collected after operations finish rather than leaking into an uncollectable static arena.

5. **CPU Time Tradeoff (+15.1%):**
   Aggregate multi-core CPU time increased from 151s to 174s due to managed heap allocation and background GC sweeping. On modern multi-core systems with surplus CPU capacity for I/O tasks, this tradeoff is highly favorable because wall-clock execution time and memory footprint both improved.

### Final Verdict
Refactoring `FullPath` to a `sealed class` is a clear architectural and performance win: it produces **higher throughput**, **lower memory retention**, **shorter GC pauses**, and **eliminates ~150 lines of complex arena bookkeeping code**.
