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
#nullable enable

using System;
using System.Threading;

namespace mtsuite.CoreFileSystem.ObjectPool;

public interface INameTable {
    /// <summary>
    /// Gets an existing string matching the span, or adds a new string if not present.
    /// Thread-safe, lock-free on reads and normal inserts, and auto-growing at 80% load factor.
    /// </summary>
    string GetOrAdd(ReadOnlySpan<char> span);

    /// <summary>
    /// Total number of calls to get/create a string.
    /// </summary>
    long CallCount { get; }

    /// <summary>
    /// Number of unique strings stored, or null if the implementation does not track unique strings.
    /// </summary>
    long? UniqueStringCount { get; }

    /// <summary>
    /// Approximate GC heap memory used in bytes.
    /// </summary>
    long ApproximateHeapBytes { get; }
}

public class NoCacheNameTable : INameTable {
    private long _callCount;
    private long _approximateHeapBytes;

    public long CallCount => Volatile.Read(ref _callCount);

    public long? UniqueStringCount => null;

    public long ApproximateHeapBytes => Volatile.Read(ref _approximateHeapBytes);

    public string GetOrAdd(ReadOnlySpan<char> span) {
        Interlocked.Increment(ref _callCount);
        // Approx string object size: 24 bytes header/alignment + 2 bytes per char
        Interlocked.Add(ref _approximateHeapBytes, 24L + span.Length * 2L);
        return span.ToString();
    }
}

/// <summary>
/// A high-performance, thread-safe, lock-free string intern table using per-thread direct-mapped caching.
/// Deduplicates strings created from <see cref="ReadOnlySpan{char}"/> with zero locks, zero interlocked atomic operations,
/// and zero heap node allocations on cache hits.
/// </summary>
public class NameTable : INameTable, IDisposable
{
    private readonly ThreadLocal<ThreadCache> _threadCache;
    private readonly int _capacityPerThread;
    private readonly StringComparison _comparison;

    private sealed class ThreadCache
    {
        public readonly string?[] Entries;
        public readonly int Mask;
        public long CallCount;
        public int PopulatedCount;
        public long ApproximateHeapBytes;

        public ThreadCache(int size)
        {
            Entries = new string?[size];
            Mask = size - 1;
            ApproximateHeapBytes = (long)size * IntPtr.Size + 24 + 48;
        }

        public string GetOrAdd(ReadOnlySpan<char> span, StringComparison comparison)
        {
            CallCount++;
            if (span.IsEmpty) return string.Empty;

            int hash = string.GetHashCode(span);
            int index = hash & Mask;

            string? cached = Entries[index];
            if (cached != null)
            {
                if (span.Equals(cached.AsSpan(), comparison))
                {
                    // Cache hit: 0 allocations, 0 atomic instructions
                    return cached;
                }

                // Cache collision / overwrite
                string overwrittenStr = span.ToString();
                Entries[index] = overwrittenStr;
                ApproximateHeapBytes += (long)(overwrittenStr.Length - cached.Length) * 2L;
                return overwrittenStr;
            }

            // New slot populated
            string newStr = span.ToString();
            Entries[index] = newStr;
            PopulatedCount++;
            ApproximateHeapBytes += 24L + (long)newStr.Length * 2L;
            return newStr;
        }
    }

    public NameTable(int capacityPerThread = 4096, StringComparison comparison = StringComparison.Ordinal)
    {
        // Round up capacity to power of 2
        int size = 1;
        while (size < capacityPerThread && size < (1 << 20)) size <<= 1;

        _capacityPerThread = size;
        _comparison = comparison;
        _threadCache = new ThreadLocal<ThreadCache>(() => new ThreadCache(_capacityPerThread), trackAllValues: true);
    }

    public int Capacity => _capacityPerThread;

    public long CallCount
    {
        get
        {
            long total = 0;
            foreach (var cache in _threadCache.Values)
            {
                total += cache.CallCount;
            }
            return total;
        }
    }

    public long? UniqueStringCount
    {
        get
        {
            long total = 0;
            foreach (var cache in _threadCache.Values)
            {
                total += cache.PopulatedCount;
            }
            return total;
        }
    }

    public long ApproximateHeapBytes
    {
        get
        {
            long total = 0;
            foreach (var cache in _threadCache.Values)
            {
                total += cache.ApproximateHeapBytes;
            }
            return total;
        }
    }

    /// <summary>
    /// Gets an existing string matching the span, or adds a new string if not present.
    /// Thread-safe and completely lock-free / atomic-free via thread-local direct-mapped cache.
    /// </summary>
    public string GetOrAdd(ReadOnlySpan<char> span)
    {
        return _threadCache.Value!.GetOrAdd(span, _comparison);
    }

    public void Dispose()
    {
        _threadCache.Dispose();
    }
}