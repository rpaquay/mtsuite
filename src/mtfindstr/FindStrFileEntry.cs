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
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtfindstr {
  public class FindStrFileEntry {
    private readonly string _pattern;
    private readonly byte[] _patternBytes;
    private readonly IPool<IList<FindStrEntry>> _listPool;
    private readonly IPool<byte[]> _byteArrayPool;

    public FindStrFileEntry(string pattern, MtPoolFactory poolFactory) {
      _pattern = pattern;
      _patternBytes = System.Text.Encoding.UTF8.GetBytes(pattern);
      _listPool = poolFactory.Create<IList<FindStrEntry>>("FindStrEntries", () => new List<FindStrEntry>(), static list => list.Clear());
      _byteArrayPool = poolFactory.Create("FileIOByteArrayPool", static () => new byte[FileIOByteArrayPool.BufferSize]);
    }

    /// <summary>
    /// Searches the specified file for occurrences of the search pattern.
    /// <para>This implementation is optimized for maximum efficiency, speed, and low memory usage under the assumption that text files are UTF-8 encoded:</para>
    /// <list type="number">
    /// <item>
    /// <description><b>Zero String/Text Decoding Allocation:</b> The search is performed directly on raw bytes from the file rather than using a StreamReader (which decodes the entire stream into UTF-16 characters and strings).</description>
    /// </item>
    /// <item>
    /// <description><b>Buffer Pooling:</b> Standardized on the shared high-throughput FileIOByteArrayPool (1 MB buffers) from the factory, resulting in zero buffer allocations per file search.</description>
    /// </item>
    /// <item>
    /// <description><b>High-Speed Vectorized Matching:</b> Utilizes .NET 8 vectorized ReadOnlySpan&lt;byte&gt;.IndexOf for ultra-fast pattern matching inside the byte stream.</description>
    /// </item>
    /// <item>
    /// <description><b>Simplification of Boundary Handling:</b> If a match spans across two buffer blocks, the boundary is handled by copying the last patternLength - 1 bytes of the current block to the beginning of the next block. This avoids complex partial-match state machines and guarantees contiguous search spans.</description>
    /// </item>
    /// <item>
    /// <description><b>UTF-8 Conformant Column Calculations:</b> Newline bytes (\n) are identified at the byte level. Character columns are computed by skipping UTF-8 continuation bytes ((b &amp; 0xC0) == 0x80) from the start of the line to the match index, producing accurate character-based offsets instead of raw byte offsets.</description>
    /// </item>
    /// <item>
    /// <description><b>Heuristic Binary File Check:</b> Performs a fast ratio-based ASCII check on the first block to identify and skip binary files.</description>
    /// </item>
    /// </list>
    /// </summary>
    public FromPool<IList<FindStrEntry>> SearchFile(IFileSystem fileSystem, FileSystemEntry entry) {
      if (!entry.IsFile) {
        return _listPool.AllocateFrom();
      }

      // Skip small files
      if (_patternBytes.Length > entry.FileSize) {
        return _listPool.AllocateFrom();
      }

      bool fallback = false;
      var result = _listPool.AllocateFrom();
      try {
        using (var stream = fileSystem.OpenFile(entry.Path, FileAccess.Read)) {
          using (var bufferFromPool = _byteArrayPool.AllocateFrom()) {
            byte[] buffer = bufferFromPool.Item;
            int patternLength = _patternBytes.Length;

            int bufferOffset = 0;
            long streamOffset = 0; // Cumulative file byte offset of buffer[0]
            int lineIndex = 0;
            int charIndexInLine = 0;
            int currentScanOffset = 0;

            while (true) {
              int bytesToRead = buffer.Length - bufferOffset;
              int bytesRead = stream.Read(buffer, bufferOffset, bytesToRead);
              if (bytesRead == 0 && bufferOffset == 0) {
                break;
              }

              int totalBytes = bufferOffset + bytesRead;
              if (totalBytes < patternLength) {
                // Not enough bytes to match the pattern
                break;
              }

              // Check if valid UTF-8 on the first block
              if (streamOffset == 0 && totalBytes > 0) {
                // If it contains a null byte, it is binary. Skip it immediately.
                if (Array.IndexOf(buffer, (byte)0, 0, Math.Min(totalBytes, 8000)) >= 0) {
                  break;
                }

                if (!IsValidUtf8(buffer, totalBytes)) {
                  // Not valid UTF-8. Could be legacy text or binary.
                  // Distinguish them using the ASCII ratio check.
                  int asciiCount = 0;
                  int checkLength = Math.Min(totalBytes, 8000);
                  for (int i = 0; i < checkLength; i++) {
                    byte b = buffer[i];
                    if ((b >= 32 && b <= 126) || b == 10 || b == 13 || b == 9) {
                      asciiCount++;
                    }
                  }
                  if ((float)asciiCount / checkLength <= 0.8f) {
                    // Binary file, skip it
                    break;
                  }

                  // Legacy text file, fallback to StreamReader
                  fallback = true;
                  break;
                }
              }

              ReadOnlySpan<byte> searchSpan = new ReadOnlySpan<byte>(buffer, 0, totalBytes);
              ReadOnlySpan<byte> patternSpan = _patternBytes;

              int searchOffset = 0;
              while (searchOffset <= totalBytes - patternLength) {
                int matchIndex = searchSpan.Slice(searchOffset).IndexOf(patternSpan);
                if (matchIndex < 0) {
                  break;
                }

                int absoluteMatchIndex = searchOffset + matchIndex;

                // Scan up to absoluteMatchIndex to update lineIndex and charIndexInLine
                for (int i = currentScanOffset; i < absoluteMatchIndex; i++) {
                  byte b = buffer[i];
                  if ((b & 0xC0) != 0x80) {
                    charIndexInLine++;
                  }
                  if (b == '\n') {
                    lineIndex++;
                    charIndexInLine = 0;
                  }
                }
                currentScanOffset = absoluteMatchIndex;

                result.Item.Add(new FindStrEntry {
                  LineNumber = lineIndex + 1,
                  ColumnNumber = charIndexInLine + 1
                });

                searchOffset = absoluteMatchIndex + patternLength;
              }

              if (bytesRead == 0) {
                break;
              }

              // Prepare for the next block
              int boundaryOffset = totalBytes - (patternLength - 1);

              // Scan up to boundaryOffset to update running counts for the next block
              for (int i = currentScanOffset; i < boundaryOffset; i++) {
                byte b = buffer[i];
                if ((b & 0xC0) != 0x80) {
                  charIndexInLine++;
                }
                if (b == '\n') {
                  lineIndex++;
                  charIndexInLine = 0;
                }
              }

              int lineIndexAtBoundary = lineIndex;
              int charIndexAtBoundary = charIndexInLine;

              // Copy the last patternLength - 1 bytes to the beginning of the buffer
              int bytesToCopy = patternLength - 1;
              Array.Copy(buffer, boundaryOffset, buffer, 0, bytesToCopy);

              bufferOffset = bytesToCopy;
              streamOffset += boundaryOffset;
              lineIndex = lineIndexAtBoundary;
              charIndexInLine = charIndexAtBoundary;
              currentScanOffset = 0;
            }
          }
        }
      } catch {
        result.Dispose();
        throw;
      }

      if (fallback) {
        result.Dispose();
        return SearchFileFallback(fileSystem, entry);
      }

      return result;
    }

    private FromPool<IList<FindStrEntry>> SearchFileFallback(IFileSystem fileSystem, FileSystemEntry entry) {
      using (var stream = fileSystem.OpenFile(entry.Path, FileAccess.Read)) {
        using (var reader = new StreamReader(stream)) {
          var searcher = new FindStrStream();
          return searcher.Search(reader, _pattern.ToCharArray(), _listPool);
        }
      }
    }

    private static bool IsValidUtf8(byte[] buffer, int length) {
      int i = 0;
      while (i < length) {
        byte b1 = buffer[i++];
        if (b1 < 0x80) {
          continue; // ASCII
        }

        // Validate multi-byte sequence
        if (b1 >= 0xC2 && b1 <= 0xDF) {
          if (i >= length) return true; // Accept partial match at boundary
          byte b2 = buffer[i++];
          if ((b2 & 0xC0) != 0x80) return false;
        } else if (b1 >= 0xE0 && b1 <= 0xEF) {
          if (i >= length) return true;
          byte b2 = buffer[i++];
          if ((b2 & 0xC0) != 0x80) return false;
          
          if (i >= length) return true;
          byte b3 = buffer[i++];
          if ((b3 & 0xC0) != 0x80) return false;
        } else if (b1 >= 0xF0 && b1 <= 0xF4) {
          if (i >= length) return true;
          byte b2 = buffer[i++];
          if ((b2 & 0xC0) != 0x80) return false;
          
          if (i >= length) return true;
          byte b3 = buffer[i++];
          if ((b3 & 0xC0) != 0x80) return false;
          
          if (i >= length) return true;
          byte b4 = buffer[i++];
          if ((b4 & 0xC0) != 0x80) return false;
        } else {
          return false; // Invalid UTF-8 lead byte
        }
      }
      return true;
    }
  }
}
