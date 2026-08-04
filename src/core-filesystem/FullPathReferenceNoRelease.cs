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
using System.Runtime.CompilerServices;
using System.Threading;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// A lightweight 4-byte index-based reference to a parent <see cref="FullPath"/> stored in
/// <see cref="FullPathReferenceNoRelease"/>, eliminating heap object allocations and GC overhead.
/// </summary>
public readonly struct FullPathReference : IEquatable<FullPathReference>
{
    public static readonly FullPathReference Null = default;

    private readonly int _index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FullPathReference(int index)
    {
        _index = index;
    }

    public int Index => _index;

    public bool IsNull => _index == 0;

    public FullPath FullPath => FullPathReferenceNoRelease.Get(_index);

    public bool Equals(FullPathReference other)
    {
        if (_index == other._index)
            return true;
        if (_index == 0 || other._index == 0)
            return false;
        return FullPath.Equals(other.FullPath);
    }

    public override bool Equals(object? obj) => obj is FullPathReference other && Equals(other);

    public override int GetHashCode()
    {
        if (_index == 0)
            return 0;
        return FullPath.GetHashCode();
    }

    public static bool operator ==(FullPathReference left, FullPathReference right) => left.Equals(right);

    public static bool operator !=(FullPathReference left, FullPathReference right) => !left.Equals(right);
}

/// <summary>
/// High-performance, lock-free append-only arena for storing parent <see cref="FullPath"/> instances.
/// Uses a paged/chunked array to avoid Large Object Heap (LOH) allocations and array resizing copies.
/// </summary>
public static class FullPathReferenceNoRelease
{
    private const int ChunkBits = 12;
    private const int ChunkSize = 1 << ChunkBits; // 4096 entries (64 KB, safely below 85 KB LOH)
    private const int ChunkMask = ChunkSize - 1;

    // Up to 4096 chunks = 16.77 million parent entries
    private static readonly FullPath[][] s_chunks = new FullPath[4096][];
    private static int s_count = 0; // Index 0 is reserved for Null / Root

    static FullPathReferenceNoRelease()
    {
        s_chunks[0] = new FullPath[ChunkSize];
    }

    /// <summary>
    /// Allocates a parent <see cref="FullPath"/> entry in the arena and returns its <see cref="FullPathReference"/>.
    /// </summary>
    public static FullPathReference Allocate(FullPath parent)
    {
        int index = Interlocked.Increment(ref s_count);
        int chunkIdx = index >> ChunkBits;
        int itemOffset = index & ChunkMask;

        var chunk = Volatile.Read(ref s_chunks[chunkIdx]);
        if (chunk == null)
        {
            var newChunk = new FullPath[ChunkSize];
            Interlocked.CompareExchange(ref s_chunks[chunkIdx], newChunk, null);
            chunk = s_chunks[chunkIdx];
        }

        chunk[itemOffset] = parent;
        return new FullPathReference(index);
    }

    /// <summary>
    /// Retrieves the <see cref="FullPath"/> stored at the given 1-based index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FullPath Get(int index)
    {
        if (index == 0)
            throw new InvalidOperationException("Root path does not have a parent reference.");

        return s_chunks[index >> ChunkBits][index & ChunkMask];
    }

    /// <summary>
    /// Total number of parent <see cref="FullPath"/> entries allocated in the arena.
    /// </summary>
    public static int AllocatedCount => s_count;

    /// <summary>
    /// Total number of 4,096-entry chunks allocated.
    /// </summary>
    public static int ChunkCount => s_count == 0 ? 0 : ((s_count - 1) >> ChunkBits) + 1;

    /// <summary>
    /// Total memory capacity allocated by chunks in bytes.
    /// </summary>
    public static long AllocatedBytes => (long)ChunkCount * ChunkSize * Unsafe.SizeOf<FullPath>();
}
