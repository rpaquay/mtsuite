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
/// A lightweight, heap-storable string slice pointing to a segment of a char array.
/// Can be stored in standard collections (<see cref="System.Collections.Generic.List{T}"/>)
/// and converted to <see cref="ReadOnlySpan{char}"/> without heap allocation.
/// </summary>
public readonly struct StringSlice : IEquatable<StringSlice>, IComparable<StringSlice>
{
    public static readonly StringSlice Empty = new StringSlice(Array.Empty<char>(), 0, 0);

    private readonly char[] _buffer;
    private readonly int _offset;
    private readonly int _length;

    public StringSlice(char[] buffer, int offset, int length)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0 || offset + length > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(length));

        _buffer = buffer;
        _offset = offset;
        _length = length;
    }

    public char[] Buffer => _buffer;
    public int Offset => _offset;
    public int Length => _length;
    public bool IsEmpty => _length == 0;

    public char this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_length)
                throw new IndexOutOfRangeException();
            return _buffer[_offset + index];
        }
    }

    public ReadOnlySpan<char> Span => _buffer != null ? _buffer.AsSpan(_offset, _length) : ReadOnlySpan<char>.Empty;

    public ReadOnlyMemory<char> Memory => _buffer != null ? _buffer.AsMemory(_offset, _length) : ReadOnlyMemory<char>.Empty;

    public static implicit operator ReadOnlySpan<char>(StringSlice slice) => slice.Span;

    public static implicit operator ReadOnlyMemory<char>(StringSlice slice) => slice.Memory;

    public bool Equals(StringSlice other)
    {
        return Span.SequenceEqual(other.Span);
    }

    public bool Equals(ReadOnlySpan<char> other, StringComparison comparison = StringComparison.Ordinal)
    {
        return Span.Equals(other, comparison);
    }

    public bool Equals(string? other, StringComparison comparison = StringComparison.Ordinal)
    {
        if (other == null) return IsEmpty;
        return Span.Equals(other.AsSpan(), comparison);
    }

    public override bool Equals(object? obj)
    {
        return obj is StringSlice other && Equals(other);
    }

    public override int GetHashCode()
    {
        return string.GetHashCode(Span, StringComparison.Ordinal);
    }

    public int GetHashCode(StringComparison comparison)
    {
        return string.GetHashCode(Span, comparison);
    }

    public int CompareTo(StringSlice other)
    {
        return Span.SequenceCompareTo(other.Span);
    }

    public int CompareTo(StringSlice other, StringComparison comparison)
    {
        return Span.CompareTo(other.Span, comparison);
    }

    public override string ToString()
    {
        return _buffer != null && _length > 0 ? new string(_buffer, _offset, _length) : string.Empty;
    }

    public static bool operator ==(StringSlice left, StringSlice right) => left.Equals(right);
    public static bool operator !=(StringSlice left, StringSlice right) => !left.Equals(right);
}
