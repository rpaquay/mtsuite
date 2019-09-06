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

using System.IO;

namespace mtgrep {
  public class GrepFileStream {
    private readonly FileStream _stream;
    private readonly byte[] _buffer = new byte[1_024];
    private int _bufferLength;
    private int _bufferOffset;

    public GrepFileStream(FileStream stream) {
      _stream = stream;
    }

    public bool IsBinary() {
      EnsureBuffer();
      int asciiCount = 0;
      for (var i = 0; i < _bufferLength; i++) {
        if (IsAscii(_buffer[i])) {
          asciiCount++;
        }
      }

      float asciiRatio = (float)asciiCount / (float)_bufferLength;
      RestartBuffer();
      return asciiRatio <= 0.8;
    }

    private void RestartBuffer() {
      _bufferOffset = 0;
    }

    private static bool IsAscii(byte v) {
      return (v >= 32 && v <= 126);
    }

    private void EnsureBuffer() {
      if (_bufferOffset >= _bufferLength) {
        var count = _stream.Read(_buffer, 0, _buffer.Length);
        _bufferLength = count;
        _bufferOffset = 0;
      }
    }
  }
}
