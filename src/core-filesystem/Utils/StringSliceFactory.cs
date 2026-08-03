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

namespace mtsuite.CoreFileSystem.Utils;

/// <summary>
/// A high-performance arena allocator for creating <see cref="StringSlice"/> instances
/// by packing characters into large contiguous char buffer chunks (default 256 KB = 128K chars).
/// </summary>
public class StringSliceFactory
{
    /// <summary>
    /// Default buffer chunk size in chars: 128 * 1024 chars = 256 KB.
    /// </summary>
    public const int DefaultBufferSizeChars = 128 * 1024;

    private readonly object _lock = new object();
    private readonly int _chunkSize;
    private readonly List<char[]> _chunks = new List<char[]>();
    private char[] _currentChunk;
    private int _currentOffset;

    public StringSliceFactory(int chunkSizeInChars = DefaultBufferSizeChars)
    {
        if (chunkSizeInChars < 1)
            throw new ArgumentOutOfRangeException(nameof(chunkSizeInChars), "Chunk size must be >= 1");

        _chunkSize = chunkSizeInChars;
        _currentChunk = new char[_chunkSize];
        _chunks.Add(_currentChunk);
        _currentOffset = 0;
    }

    /// <summary>
    /// Number of large buffer chunks allocated by this factory.
    /// </summary>
    public int ChunkCount
    {
        get
        {
            lock (_lock)
            {
                return _chunks.Count;
            }
        }
    }

    /// <summary>
    /// Total characters allocated across all chunks.
    /// </summary>
    public long TotalCapacityChars
    {
        get
        {
            lock (_lock)
            {
                return (long)_chunks.Count * _chunkSize;
            }
        }
    }

    /// <summary>
    /// Creates a <see cref="StringSlice"/> from a <see cref="ReadOnlySpan{char}"/>.
    /// Copies the span into the current large buffer chunk without creating a new string.
    /// </summary>
    public StringSlice Create(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return StringSlice.Empty;

        lock (_lock)
        {
            int length = span.Length;

            // If string is larger than standard chunk size, allocate a dedicated buffer
            if (length > _chunkSize)
            {
                var dedicated = new char[length];
                span.CopyTo(dedicated);
                _chunks.Add(dedicated);
                return new StringSlice(dedicated, 0, length);
            }

            // If current chunk does not have enough remaining space, allocate a new chunk
            if (_currentOffset + length > _currentChunk.Length)
            {
                _currentChunk = new char[_chunkSize];
                _chunks.Add(_currentChunk);
                _currentOffset = 0;
            }

            int offset = _currentOffset;
            span.CopyTo(_currentChunk.AsSpan(offset, length));
            _currentOffset += length;

            return new StringSlice(_currentChunk, offset, length);
        }
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

    /// <summary>
    /// Resets the allocator offset so previous memory can be overwritten, avoiding re-allocation.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            if (_chunks.Count > 1)
            {
                var first = _chunks[0];
                _chunks.Clear();
                _chunks.Add(first);
                _currentChunk = first;
            }
            _currentOffset = 0;
        }
    }
}
