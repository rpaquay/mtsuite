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

namespace mtsuite.CoreFileSystem.Utils;

/// <summary>
/// A high-performance lock-free arena allocator for creating <see cref="StringSlice"/> instances
/// by packing characters into thread-local contiguous char buffer chunks (< 8 KB).
/// Uses [ThreadStatic] for zero-contention, lock-free bump allocations.
/// </summary>
public class StringSliceFactory
{
    /// <summary>
    /// Default buffer chunk size in chars: 4,000 chars = 8,000 bytes (slightly under 8 KB).
    /// </summary>
    public const int DefaultBufferSizeChars = 4000;

    private readonly int _chunkSize;

    [ThreadStatic]
    private static ThreadState? t_threadState;

    private sealed class ThreadState
    {
        public char[] CurrentChunk;
        public int CurrentOffset;

        public ThreadState(int chunkSize)
        {
            CurrentChunk = new char[chunkSize];
            CurrentOffset = 0;
        }
    }

    public StringSliceFactory(int chunkSizeInChars = DefaultBufferSizeChars)
    {
        if (chunkSizeInChars < 1)
            throw new ArgumentOutOfRangeException(nameof(chunkSizeInChars), "Chunk size must be >= 1");

        _chunkSize = chunkSizeInChars;
    }

    /// <summary>
    /// Creates a <see cref="StringSlice"/> from a <see cref="ReadOnlySpan{char}"/>.
    /// Copies the span into the current thread-local buffer chunk without locks or heap allocations.
    /// </summary>
    public StringSlice Create(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return StringSlice.Empty;

        int length = span.Length;
        var state = t_threadState;
        if (state == null || state.CurrentChunk.Length != _chunkSize)
        {
            state = new ThreadState(_chunkSize);
            t_threadState = state;
        }

        // If string is larger than standard chunk size, allocate a dedicated buffer
        if (length > _chunkSize)
        {
            var dedicated = new char[length];
            span.CopyTo(dedicated);
            return new StringSlice(dedicated, 0, length);
        }

        // If current thread-local chunk does not have enough remaining space, allocate a new chunk
        if (state.CurrentOffset + length > state.CurrentChunk.Length)
        {
            state.CurrentChunk = new char[_chunkSize];
            state.CurrentOffset = 0;
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
}
