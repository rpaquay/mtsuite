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
/// A high-performance, thread-safe, auto-growing string intern table
/// for deduplicating strings created from <see cref="ReadOnlySpan{char}"/> without heap allocation on cache hits.
///
/// Note: this NameTable implementation has shown to decrease GC pressure (i.e. fewer Gen2 GC passes),
/// but decrease overall performance (probably due to the hashing and table lookup overhead), so it is not-used
/// in "mtsuite" for now
/// </summary>
public class NameTable : INameTable
{
    private const int MaxCapacity = 1 << 30;
    private const float LoadFactorThreshold = 0.80f;

    private readonly object _resizeLock = new object();
    private readonly StringComparison _comparison;
    private volatile BucketTable _table;
    private int _count;
    private long _callCount;
    private long _totalStringChars;

    private sealed class Entry
    {
        public readonly string Value;
        public readonly int HashCode;
        public readonly Entry? Next;

        public Entry(string value, int hashCode, Entry? next)
        {
            Value = value;
            HashCode = hashCode;
            Next = next;
        }
    }

    private sealed class BucketTable
    {
        public readonly Entry?[] Entries;
        public readonly int Mask;
        public readonly int GrowthThreshold;

        public BucketTable(int size)
        {
            Entries = new Entry?[size];
            Mask = size - 1;
            GrowthThreshold = (int)(size * LoadFactorThreshold);
        }
    }

    public NameTable(int initialCapacity = 2048, StringComparison comparison = StringComparison.Ordinal)
    {
        // Round up capacity to power of 2 for fast bitwise masking (hash & mask)
        int size = 1;
        while (size < initialCapacity && size < MaxCapacity) size <<= 1;

        _table = new BucketTable(size);
        _comparison = comparison;
    }

    public int Count => Volatile.Read(ref _count);

    public int Capacity => _table.Entries.Length;

    public long CallCount => Volatile.Read(ref _callCount);

    public long? UniqueStringCount => Volatile.Read(ref _count);

    public long ApproximateHeapBytes
    {
        get
        {
            var count = (long)Volatile.Read(ref _count);
            var table = _table;
            var bucketBytes = (long)table.Entries.Length * IntPtr.Size + 24;
            var entryBytes = count * 40L;
            var stringBytes = count * 24L + Volatile.Read(ref _totalStringChars) * 2L;
            return bucketBytes + entryBytes + stringBytes;
        }
    }

    /// <summary>
    /// Gets an existing string matching the span, or adds a new string if not present.
    /// Thread-safe, lock-free on reads and normal inserts, and auto-growing at 80% load factor.
    /// </summary>
    public string GetOrAdd(ReadOnlySpan<char> span)
    {
        Interlocked.Increment(ref _callCount);
        if (span.IsEmpty) return string.Empty;

        // 1. Calculate string hash code directly from Span without allocating
        int hashCode = string.GetHashCode(span);

        // 2. Lock-free fast read path
        BucketTable table = _table;
        int index = hashCode & table.Mask;

        for (Entry? e = Volatile.Read(ref table.Entries[index]); e != null; e = e.Next)
        {
            if (e.HashCode == hashCode && span.Equals(e.Value.AsSpan(), _comparison))
            {
                // ✅ Found in cache: Return existing string (0 heap allocations)
                return e.Value;
            }
        }

        // 3. Cache Miss: Allocate string once and atomically insert into bucket
        string newString = span.ToString();

        while (true)
        {
            table = _table;
            index = hashCode & table.Mask;

            Entry? currentHead = Volatile.Read(ref table.Entries[index]);

            // Re-check bucket in case another thread inserted the same string concurrently
            for (Entry? e = currentHead; e != null; e = e.Next)
            {
                if (e.HashCode == hashCode && span.Equals(e.Value.AsSpan(), _comparison))
                {
                    return e.Value;
                }
            }

            var newEntry = new Entry(newString, hashCode, currentHead);
            if (Interlocked.CompareExchange(ref table.Entries[index], newEntry, currentHead) == currentHead)
            {
                // If table was resized concurrently, ensure this exact entry exists in the new active table
                if (_table != table)
                {
                    EnsureInActiveTable(newEntry);
                }

                Interlocked.Add(ref _totalStringChars, newString.Length);
                int newCount = Interlocked.Increment(ref _count);
                if (newCount > table.GrowthThreshold)
                {
                    TryGrow(table);
                }

                return newString;
            }
            // CAS failed due to concurrent modification: loop and retry
        }
    }

    private void EnsureInActiveTable(Entry entry)
    {
        while (true)
        {
            BucketTable activeTable = _table;
            int index = entry.HashCode & activeTable.Mask;

            Entry? currentHead = Volatile.Read(ref activeTable.Entries[index]);
            for (Entry? e = currentHead; e != null; e = e.Next)
            {
                if (e.HashCode == entry.HashCode && entry.Value.AsSpan().Equals(e.Value.AsSpan(), _comparison))
                {
                    // Already present in new active table
                    return;
                }
            }

            var newEntry = new Entry(entry.Value, entry.HashCode, currentHead);
            if (Interlocked.CompareExchange(ref activeTable.Entries[index], newEntry, currentHead) == currentHead)
            {
                if (_table == activeTable)
                {
                    return;
                }
                // Table grew again concurrently, loop and retry in latest table
            }
        }
    }

    private void TryGrow(BucketTable currentTable)
    {
        if (currentTable.Entries.Length >= MaxCapacity)
            return;

        if (Monitor.TryEnter(_resizeLock))
        {
            try
            {
                // If another thread already resized it, exit
                if (_table != currentTable)
                    return;

                int newSize = currentTable.Entries.Length * 2;
                if (newSize > MaxCapacity || newSize < currentTable.Entries.Length)
                    return;

                var newTable = new BucketTable(newSize);

                // Initial rehash of all entries from currentTable into newTable
                for (int i = 0; i < currentTable.Entries.Length; i++)
                {
                    for (Entry? e = Volatile.Read(ref currentTable.Entries[i]); e != null; e = e.Next)
                    {
                        int newIndex = e.HashCode & newTable.Mask;
                        newTable.Entries[newIndex] = new Entry(e.Value, e.HashCode, newTable.Entries[newIndex]);
                    }
                }

                // Publish new resized table
                _table = newTable;

                // Post-publish rescan: catch any entries inserted into currentTable while copying
                for (int i = 0; i < currentTable.Entries.Length; i++)
                {
                    for (Entry? e = Volatile.Read(ref currentTable.Entries[i]); e != null; e = e.Next)
                    {
                        int newIndex = e.HashCode & newTable.Mask;
                        bool found = false;
                        for (Entry? n = Volatile.Read(ref newTable.Entries[newIndex]); n != null; n = n.Next)
                        {
                            if (ReferenceEquals(n.Value, e.Value) || (n.HashCode == e.HashCode && n.Value.AsSpan().Equals(e.Value.AsSpan(), _comparison)))
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            while (true)
                            {
                                Entry? head = Volatile.Read(ref newTable.Entries[newIndex]);
                                var rehashed = new Entry(e.Value, e.HashCode, head);
                                if (Interlocked.CompareExchange(ref newTable.Entries[newIndex], rehashed, head) == head)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                Monitor.Exit(_resizeLock);
            }
        }
    }
}