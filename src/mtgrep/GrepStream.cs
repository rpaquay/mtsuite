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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace mtgrep {
  public class GrepStream {
    private const int StreamBufferSize = 64 * 1024;
    private static readonly ReadOnlyCollection<GrepEntry> _emptyResult = new ReadOnlyCollection<GrepEntry>(new List<GrepEntry>());

    private readonly char[] _buffer = new char[StreamBufferSize];
    private int _bufferLength;
    private int _bufferOffset;
    private long _lastLineStartStreamOffset;
    private long _bufferStreamOffset;
    private int _lineIndex;
    private int _columnIndex;

    public bool EOF => _bufferLength == 0;

    public static ReadOnlyCollection<GrepEntry> EmptyResult => _emptyResult;

    public GrepStream() : this(StreamBufferSize) {

    }

    public GrepStream(int buffersize) {
      _buffer = new char[buffersize];
    }

    public IList<GrepEntry> Search(StreamReader stream, char[] patternArray) {
      Reset();
      EnsureBuffer(stream);

      if (IsBinary()) {
        return EmptyResult;
      }

      // Create collection lazily in case there are no matches
      IList<GrepEntry> result = null;
      while (true) {
        var grepEntry = FindNextEntry(stream, patternArray);
        if (grepEntry == null) {
          break;
        }
        // Create collection now if needed
        if (result == null) {
          result = new List<GrepEntry>(); ;
        }
        result.Add(grepEntry);
      }
      return result ?? EmptyResult;
    }

    private void Reset() {
      _bufferLength = 0;
      _bufferOffset = 0;
      _lineIndex = 0;
      _columnIndex = 0;
      _lastLineStartStreamOffset = 0;
      _bufferStreamOffset = 0;
    }

    private GrepEntry FindNextEntry(StreamReader stream, char[] patternArray) {
      while (!EOF) {
        int patternIndex = SearchChars(
          _buffer, _bufferOffset, _bufferLength - _bufferOffset,
          patternArray, 0, patternArray.Length);
        if (patternIndex >= 0) {
          UpdatePosition(_bufferOffset, patternIndex);
          _bufferOffset = patternIndex + patternArray.Length;
          return new GrepEntry() {
            LineNumber = _lineIndex + 1,
            ColumnNumber = _columnIndex + 1,
          };
        } else {
          _bufferOffset = _bufferLength;
          EnsureBuffer(stream);
        }
      }
      return null;
    }

    private void UpdatePosition(int bufferStartIndex, int patternIndex) {
      while (bufferStartIndex < patternIndex) {
        int newLineBufferIndex = SearchChar(_buffer, bufferStartIndex, patternIndex - bufferStartIndex, '\n');
        if (newLineBufferIndex < 0) {
          break;
        }
        _lastLineStartStreamOffset = _bufferStreamOffset + newLineBufferIndex + 1;
        _lineIndex++;
        bufferStartIndex = newLineBufferIndex + 1;
      }
      _columnIndex = (int)(_bufferStreamOffset + patternIndex - _lastLineStartStreamOffset);
    }

    public bool IsBinary() {
      int asciiCount = 0;
      for (var i = 0; i < _bufferLength; i++) {
        if (IsAscii(_buffer[i])) {
          asciiCount++;
        }
      }

      float asciiRatio = (float)asciiCount / (float)_bufferLength;
      return asciiRatio <= 0.8;
    }

    private static bool IsAscii(char v) {
      return (v >= 32 && v <= 126);
    }

    private void EnsureBuffer(StreamReader stream) {
      if (_bufferOffset >= _bufferLength) {
        _bufferStreamOffset += _bufferLength;
        var count = stream.Read(_buffer, 0, _buffer.Length);
        _bufferLength = count;
        _bufferOffset = 0;
      }
    }


    /// <summary>
    /// Search for the sequence of characters starting at 'needle[needleStart]'
    /// in withini the sequence of characters starting at 'haystack[haystackStart]'.
    /// <para>Returns the offset of 'needle' in 'haystack' if found.
    /// Returns -1 if not found.</para>
    /// </summary>
    private static int SearchCharsManaged(char[] haystack, int haystackStart, int haystackCount, char[] needle, int needleStart) {
      var neecleCount = needle.Length - needleStart;
      var haystackLimit = haystackCount - neecleCount;
      for (var haystackIndex = haystackStart; haystackIndex <= haystackLimit; haystackIndex++) {
        var needleIndex = needleStart;
        for (; needleIndex < neecleCount; needleIndex++) {
          if (needle[needleIndex] != haystack[haystackIndex + needleIndex]) {
            break;
          }
        }
        if (needleIndex == neecleCount) {
          return haystackIndex;
        }
      }
      return -1;
    }

    /// <summary>
    /// Search for the sequence of characters starting at 'needle[needleStart]'
    /// in withini the sequence of characters starting at 'haystack[haystackStart]'.
    /// <para>Returns the offset of 'needle' in 'haystack' if found.
    /// Returns -1 if not found.</para>
    /// </summary>
    private unsafe static int SearchChars(char[] haystack, int haystackStart, int haystackCount, char[] needle, int needleStart, int needleCount) {
      if (haystackStart + haystackCount > haystack.Length) {
        ThrowArgumentException();
      }
      if (needleStart + needleCount > needle.Length) {
        ThrowArgumentException();
      }
      // See http://github.com/dotnet/coreclr/pull/7029 to explain why
      // 1. We use "&haystack[0]" instead of "haystack"
      // 2. The "return -1" is inside the "fixed" block
      fixed (char* haystackPtr = &haystack[0]) {
        fixed (char* needlePtr = &needle[0]) {
          var haystackLimit = haystackCount - needleCount;
          for (var haystackIndex = haystackStart; haystackIndex <= haystackLimit; haystackIndex++) {
            char* haystackCurrentPtr = haystackPtr + haystackIndex;
            char* needleCurrentPtr = needlePtr;
            var needleIndex = needleStart;
            for (; needleIndex < needleCount; needleIndex++) {
              if (*needleCurrentPtr != *haystackCurrentPtr) {
                break;
              }
              haystackCurrentPtr++;
              needleCurrentPtr++;
            }
            if (needleIndex == needleCount) {
              return haystackIndex;
            }
          }
          return -1;
        }
      }
    }

    /// <summary>
    /// Highly optimized version of searching for a single character  in an array of characters.
    /// <para>Returns the position of <paramref name="needle"/> inside <paramref name="haystack"/> or -1
    /// if <paramref name="needle"/> is not present.
    /// </para>
    /// </summary>
    private unsafe static int SearchChar(char[] haystack, int haystackStart, int haystackCount, char needle) {
      if (haystackStart + haystackCount > haystack.Length) {
        ThrowArgumentException();
      }
      // See http://github.com/dotnet/coreclr/pull/7029 to explain why
      // 1. We use "&haystack[0]" instead of "haystack"
      // 2. The "return -1" is inside the "fixed" block
      fixed (char* haystackPtr = &haystack[0]) {
        char* haystackCurrent = haystackPtr + haystackStart;
        for (; haystackCount > 0; haystackCount--) {
          if (*haystackCurrent == needle) {
            break;
          }
          haystackCurrent++;
        }

        if (haystackCount > 0) {
          return (int)(haystackCurrent - haystackPtr);
        }
        return -1;
      }
    }

    private static void ThrowArgumentException() {
      throw new ArgumentException("Invalid range");
    }
  }
}
