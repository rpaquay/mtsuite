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

namespace mtsuite.shared;

public class ProgressPrinter {
    // The ANSI escape character can be represented as '\u001B' (Unicode), '\x1B' (hexadecimal),
    // or the new '\e' escape sequence introduced in C# 11.
    private const string AnsiEsc = "\u001B";

    // ANSI code to move cursor up N lines
    private const string AnsiCursorUpFormat = AnsiEsc + "[{0}A";

    private readonly object _lock = new();
    private bool _firstPrint = true;

    public void Stop() {
        lock (_lock) {
            if (!_firstPrint) {
                Console.WriteLine(); // end previously displayed line
                Console.WriteLine(); // empty line
            }
        }
    }

    public void Print(ICollection<PrinterEntry> fields) {
        var output = FieldsPrinter.BuildMultiLineOutput(fields);

        lock (_lock) {
            // Move cursor up if not first print
            if (!_firstPrint) {
                Console.Write("\r");
                if (fields.Count >= 1) {
                    Console.Write(AnsiCursorUpFormat, fields.Count - 1);
                }
            }

            _firstPrint = false;

            // Print output
            Console.Write(output);
        }
    }
}
