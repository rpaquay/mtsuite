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

namespace mtsuite.shared {
  public class ProgressPrinter {
    private readonly object _lock = new object();
    private bool _init;
    private bool _supportsPositions;
    private bool _isFirstPrint = true;
    private int _lastVisualLines;

    public void Stop() {
      if (_init) {
        Console.WriteLine(); // end previously displayed line
        Console.WriteLine(); // empty line
      }
    }

    public void Print(ICollection<PrinterEntry> fields) {
      EnsureInit();
      var output = _supportsPositions
        ? FieldsPrinter.BuildMultiLineOutput(fields)
        : FieldsPrinter.BuildSingleLineOutput(fields);
      lock (_lock) {
        if (_supportsPositions) {
          // On first print, we just write.
          // On subsequent prints, we move cursor up based on previous visual footprint.
          if (!_isFirstPrint) {
            var currentTop = Console.CursorTop;
            var top = Math.Max(0, currentTop - (_lastVisualLines - 1));
            Console.SetCursorPosition(0, top);
          }

          Console.Write(output);

          _isFirstPrint = false;
          _lastVisualLines = CountVisualLines(output);
        }
        else {
          Console.Write("\r");
          Console.Write(output);
        }
      }
    }

    private int CountVisualLines(string s) {
        if (string.IsNullOrEmpty(s)) return 0;
        
        int windowWidth = 80; // Default fallback
        try {
            windowWidth = Console.WindowWidth;
        } catch { /* ignore */ }

        if (windowWidth <= 0) windowWidth = 80;

        int visualLines = 0;
        var lines = s.Split('\n');
        foreach (var line in lines) {
            // Each logical line wraps if it's longer than windowWidth
            // Length 0 line still takes 1 visual line if it's explicitly part of the output string (except maybe trailing newline split?)
            // Actually s.Split('\n') will give empty string for trailing newline.
            
            // Standard console behavior:
            // "abc" (width 80) -> 1 line
            // "abc...<80chars>" -> 1 line (cursor moves to next line automatically if completely full? or stays at end?)
            // Console.WriteLine writes line + newline.
            // Console.Write just writes. 
            // If we write 80 chars on 80 width terminal, cursor wraps to next line start.
            // So 80 chars = 2 lines occupied (1 full + start of next)? 
            // Actually, if we write EXACTLY windowWidth chars, cursor might linger at end of line OR wrap. 
            // In C# Console.Write, usually it wraps when next char is written or if explicit newline.
            // However, usually we can approximate: Math.Ceiling(len / width).
            // But strict wrapping: if len == width, it usually takes 1 line until we write more.
            // BUT, our output usually ends with newlines for multi-line blocks.
            
            // Let's stick to safe approximation:
            // If line is empty, it takes 1 visual line (the newline that created it, but split removes delimiters).
            // Wait, Split removes \n.
            // "A\nB" -> "A", "B". 2 lines.
            // "A\n" -> "A", "". 2 lines (A and the empty line after).
            
            // Basic approximation:
            var len = line.Length;
            if (len == 0) {
                visualLines++;
            } else {
                visualLines += (len + windowWidth - 1) / windowWidth; 
            }
        }
        
        // s.Split('\n') on "A\nB" gives 2 parts. visual count should be visually rendered lines.
        // We printed "A\nB".
        // Part 1 "A": 1 visual line.
        // Part 2 "B": 1 visual line.
        // Total 2. Correct.
        
        // "A\n" -> "A", "".
        // Part 1 "A": 1 visual line.
        // Part 2 "": 1 visual line?
        // Yes, "A\n" prints A then moves to next line. So cursor is on next line.
        // So we occupy the line for A, and the line for empty.
        
        // Correction: We want to know how many lines we need to move UP to get back to start.
        // If we printed "A", cursor is at end of A. We are on same line. Visual lines = 1.
        // If we printed "A\n", cursor is on next line. Visual lines = 2.
        
        // Current logic:
        // "A" -> "A". len=1. (1+79)/80 = 1. Total 1. Correct.
        // "A\n" -> "A", "". 
        //   "A" -> 1.
        //   "" -> 1.
        //   Total 2. Correct.
        
        return visualLines;
    }

    private void EnsureInit() {
      if (_init) {
        return;
      }

      lock (_lock) {
        if (!_init) {
          _init = true;
          try {
             // Just check if we can access cursor properties
            var l = Console.CursorLeft;
            var t = Console.CursorTop;
            _supportsPositions = true;
          } catch (Exception) {
            _supportsPositions = false;
          }
        }
      }
    }
  }
}
