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
using System.Collections.Generic;
using System.Threading;

namespace mtsuite.CoreFileSystem.Utils;

/// <summary>
/// A high-performance, thread-safe, lock-free arena allocator for creating <see cref="StringSlice"/> instances
/// by packing characters into contiguous char buffer chunks (default 256 KB = 128K chars) per thread.
/// </summary>
public class StringSliceFactory : IDisposable
{
    /// <summary>
    /// Default buffer chunk size in chars: 128 * 1024 chars = 256 KB.
    /// </summary>
    public const int DefaultBufferSizeChars = 128 * 1024;

    /// <summary>
    /// Default shared <see cref="StringSliceFactory"/> instance.
    /// </summary>
    public static StringSliceFactory Default { get; } = new StringSliceFactory();

    private readonly int _chunkSize;
    private readonly ThreadLocal<ThreadState> _threadState;

    private sealed class ThreadState
    {
        public readonly List<char[]> Chunks = new List<char[]>();
        public char[] CurrentChunk;
        public int CurrentOffset;
        public long SliceCount;
        public long TotalCapacityChars;

        public ThreadState(int chunkSize)
        {
            CurrentChunk = new char[chunkSize];
            Chunks.Add(CurrentChunk);
            CurrentOffset = 0;
            TotalCapacityChars = chunkSize;
        }
    }

    public StringSliceFactory(int chunkSizeInChars = DefaultBufferSizeChars)
    {
        if (chunkSizeInChars < 1)
            throw new ArgumentOutOfRangeException(nameof(chunkSizeInChars), "Chunk size must be >= 1");

        _chunkSize = chunkSizeInChars;
        _threadState = new ThreadLocal<ThreadState>(() => new ThreadState(_chunkSize), trackAllValues: true);
    }

    /// <summary>
    /// Total number of slices allocated across all active threads.
    /// </summary>
    public long SliceCount
    {
        get
        {
            long total = 0;
            foreach (var state in _threadState.Values)
            {
                total += state.SliceCount;
            }
            return total;
        }
    }

    /// <summary>
    /// Number of buffer chunks allocated across all active threads.
    /// </summary>
    public int ChunkCount
    {
        get
        {
            int total = 0;
            foreach (var state in _threadState.Values)
            {
                total += state.Chunks.Count;
            }
            return total;
        }
    }

    /// <summary>
    /// Total characters allocated across all chunks in all threads.
    /// </summary>
    public long TotalCapacityChars
    {
        get
        {
            long total = 0;
            foreach (var state in _threadState.Values)
            {
                total += state.TotalCapacityChars;
            }
            return total;
        }
    }

    /// <summary>
    /// Total memory allocated across all chunks in bytes.
    /// </summary>
    public long AllocatedBytes => TotalCapacityChars * sizeof(char);

    /// <summary>
    /// Creates a <see cref="StringSlice"/> from a <see cref="ReadOnlySpan{char}"/>.
    /// Copies the span into the current thread's buffer chunk without creating a new string or acquiring locks.
    /// </summary>
    public StringSlice Create(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return StringSlice.Empty;

        var state = _threadState.Value!;
        state.SliceCount++;
        int length = span.Length;

        // If string is larger than standard chunk size, allocate a dedicated buffer
        if (length > _chunkSize)
        {
            var dedicated = new char[length];
            span.CopyTo(dedicated);
            state.Chunks.Add(dedicated);
            state.TotalCapacityChars += length;
            return new StringSlice(dedicated, 0, length);
        }

        // If current chunk does not have enough remaining space, allocate a new chunk
        if (state.CurrentOffset + length > state.CurrentChunk.Length)
        {
            state.CurrentChunk = new char[_chunkSize];
            state.Chunks.Add(state.CurrentChunk);
            state.CurrentOffset = 0;
            state.TotalCapacityChars += _chunkSize;
        }

        int offset = state.CurrentOffset;
        span.CopyTo(state.CurrentChunk.AsSpan(offset, length));
        state.CurrentOffset += length;

        return new StringSlice(state.CurrentChunk, offset, length);
    }

    /// <summary>
    /// Creates a <see cref="StringSlice"/> from a string.
    /// </summary>
    public StringSlice Create(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return StringSlice.Empty;

        return Create(text.AsSpan());
    }

    public void Dispose()
    {
        _threadState.Dispose();
    }
}
