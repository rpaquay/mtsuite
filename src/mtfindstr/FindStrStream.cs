// Copyright 2026 Renaud Paquay All Rights Reserved.
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
using System.IO;
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtfindstr {
  public class FindStrStream {
    private const int StreamBufferSize = 64 * 1024;
    private readonly char[] _buffer = new char[StreamBufferSize];

    public FindStrStream() : this(StreamBufferSize) {
    }

    public FindStrStream(int buffersize) {
      _buffer = new char[buffersize];
    }

    public FromPool<IList<FindStrEntry>> Search(StreamReader stream, char[] patternArray, IPool<IList<FindStrEntry>> listPool) {
      return new SingleSearcher(_buffer).Run(stream, patternArray, listPool);
    }

    private struct SingleSearcher {
      private readonly char[] _buffer;

      private int _bufferLength;
      private int _bufferOffset;
      private long _lastLineStartStreamOffset;
      private long _bufferStreamOffset;
      private int _lineIndex;
      private int _columnIndex;

      public SingleSearcher(char[] buffer) : this() {
        _buffer = buffer;
      }

      public bool EOF => _bufferLength == 0;

      public FromPool<IList<FindStrEntry>> Run(StreamReader stream, char[] patternArray, IPool<IList<FindStrEntry>> listPool) {
        if (((patternArray.Length + 1) / 2) > _buffer.Length) {
          throw new ArgumentException("Buffer should be larger than pattern");
        }
        EnsureBuffer(stream);

        var result = listPool.AllocateFrom();
        try {
          if (IsBinary()) {
            return result;
          }

          while (true) {
            var entry = FindNextEntry(stream, patternArray);
            if (entry == null) {
              break;
            }
            result.Item.Add(entry);
          }
          return result;
        } catch {
          result.Dispose();
          throw;
        }
      }

      private FindStrEntry FindNextEntry(StreamReader stream, char[] patternArray) {
        while (!EOF) {
          // Search for pattern inside current buffer
          int patternIndex = SearchChars(
            _buffer, _bufferOffset, _bufferLength - _bufferOffset,
            patternArray, 0, patternArray.Length);
          if (patternIndex >= 0) {
            UpdatePosition(_bufferOffset, patternIndex);
            _bufferOffset = patternIndex + patternArray.Length;
            EnsureBuffer(stream);
            return new FindStrEntry() {
              LineNumber = _lineIndex + 1,
              ColumnNumber = _columnIndex + 1,
            };
          }

          // Handle case where the pattern overlaps at end of current buffer to the
          // next buffer from the stream
          var entry = FindNextEntryAtEndOfBuffer(stream, patternArray);
          if (entry != null) {
            return entry;
          }
        }
        return null;
      }

      private FindStrEntry FindNextEntryAtEndOfBuffer(StreamReader stream, char[] patternArray) {
        for (var candidate = 0; candidate < patternArray.Length - 1; candidate++) {
          int patternPart1Length = patternArray.Length - 1 - candidate;
          int patternIndex = SearchChars(
            _buffer,
            _bufferLength - patternPart1Length,
            patternPart1Length,
            patternArray,
            0,
            patternPart1Length);

          // If we found the beginning of the pattern at the end of the buffer,
          // look for the end of the pattern at the beginning of the next buffer
          if (patternIndex >= 0) {
            // Update position and remember column where pattern start was found
            UpdatePosition(_bufferOffset, patternIndex);
            var patternPart1ColumnIndex = _columnIndex;

            // Read next block from the stream
            _bufferOffset = _bufferLength;
            EnsureBuffer(stream);
            if (EOF) {
              return null;
            }

            // Look for the end of the pattern at the beginning of the current buffer
            int patternPart2Length = patternArray.Length - patternPart1Length;
            patternIndex = SearchChars(
              _buffer,
              0,
              patternPart2Length,
              patternArray,
              patternPart1Length,
              patternPart2Length);
            if (patternIndex >= 0) {
              UpdatePosition(_bufferOffset, patternIndex);
              _bufferOffset = patternIndex + patternPart2Length;
              return new FindStrEntry() {
                LineNumber = _lineIndex + 1,
                ColumnNumber = patternPart1ColumnIndex + 1,
              };
            }
          }
        }
        // Fetch next buffer from stream
        UpdatePosition(_bufferOffset, _bufferLength);
        _bufferOffset = _bufferLength;
        EnsureBuffer(stream);
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
        var needleEnd = needleStart + needleCount;
        fixed (char* haystackPtr = &haystack[0]) {
          fixed (char* needlePtr = &needle[0]) {
            var haystackLimit = haystackStart + haystackCount - needleCount;
            for (var haystackIndex = haystackStart; haystackIndex <= haystackLimit; haystackIndex++) {
              char* haystackCurrentPtr = haystackPtr + haystackIndex;
              char* needleCurrentPtr = needlePtr + needleStart;
              var needleIndex = needleStart;
              for (; needleIndex < needleEnd; needleIndex++) {
                if (*needleCurrentPtr != *haystackCurrentPtr) {
                  break;
                }
                haystackCurrentPtr++;
                needleCurrentPtr++;
              }
              if (needleIndex == needleEnd) {
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
}
