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

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using mtsuite.shared.Utils;

namespace mtsuite.shared;

public class ProgressPrinter {
    // The ANSI escape character can be represented as '\u001B' (Unicode), '\x1B' (hexadecimal),
    // or the new '\e' escape sequence introduced in C# 11.
    private const string AnsiEsc = "\u001B";

    // ANSI code to move cursor up N lines
    private const string AnsiCursorUpFormat = AnsiEsc + "[{0}A";

    private static readonly Regex AnsiRegex = new(@"\u001B\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    private readonly object _lock = new();
    private bool _firstPrint = true;
    private int _lastLineCount = 0;

    /// <summary>
    /// Gets or sets whether ANSI escape sequences are supported in the current environment.
    /// Defaults to <see cref="ConsoleSupport.IsAnsiSupported"/>.
    /// </summary>
    public bool IsAnsiSupported { get; set; } = ConsoleSupport.IsAnsiSupported;

    public ProgressMode ProgressMode { get; set; } = ProgressMode.Full;

    public static string StripAnsi(string input) {
        return AnsiRegex.Replace(input, "");
    }

    public static int GetWindowWidth() {
        try {
            if (!Console.IsOutputRedirected) {
                var width = Console.WindowWidth;
                if (width > 0) return width;
            }
        } catch {
            // Ignore environment where Console.WindowWidth is unavailable
        }
        return 120;
    }

    public static int CountVisualLines(string s, int windowWidth) {
        if (string.IsNullOrEmpty(s)) return 0;
        if (windowWidth <= 0) windowWidth = 80;

        int visualLines = 0;
        var lines = s.Split('\n');
        foreach (var rawLine in lines) {
            var line = rawLine.TrimEnd('\r');
            var visibleLen = StripAnsi(line).Length;
            if (visibleLen == 0) {
                visualLines++;
            } else {
                visualLines += (visibleLen + windowWidth - 1) / windowWidth;
            }
        }
        return visualLines;
    }

    public void Stop() {
        ClearProgressBlock();
    }

    public void ClearProgressBlock() {
        if (!IsAnsiSupported) {
            return;
        }

        lock (_lock) {
            ClearProgressBlockLocked();
        }
    }

    private void ClearProgressBlockLocked() {
        if (!_firstPrint) {
            // Move cursor to start of the top line of the progress block
            Console.Write("\r");
            if (_lastLineCount > 1) {
                Console.Write(AnsiCursorUpFormat, _lastLineCount - 1);
            }
            // Clear all lines that were drawn by the progress printer
            for (int i = 0; i < _lastLineCount; i++) {
                Console.Write(AnsiEsc + "[K");
                if (i < _lastLineCount - 1) {
                    Console.WriteLine();
                }
            }
            // Move back to the top line so next output directly overwrites it
            Console.Write("\r");
            if (_lastLineCount > 1) {
                Console.Write(AnsiCursorUpFormat, _lastLineCount - 1);
            }
            _firstPrint = true;
            _lastLineCount = 0;
        }
    }

    public void PrintMessage(Action action) {
        lock (_lock) {
            if (IsAnsiSupported) {
                ClearProgressBlockLocked();
            }
            action();
        }
    }

    public void Print(ICollection<PrinterEntry> fields) {
        Print(fields, null);
    }

    public void Print(ICollection<PrinterEntry> fields, IReadOnlyList<string>? additionalLines) {
        if (!IsAnsiSupported) {
            return;
        }
        int windowWidth = GetWindowWidth();

        string output;
        if (ProgressMode == ProgressMode.Line) {
            var stripped = FieldsPrinter.BuildSingleLineOutput(fields);
            if (stripped.Length > windowWidth - 1) {
                stripped = stripped.Substring(0, windowWidth - 1);
            }
            output = AnsiEsc + "[K" + stripped;
        } else {
            int maxLineLength = Math.Max(20, windowWidth - 1);
            var sb = new System.Text.StringBuilder();

            var fieldsText = FieldsPrinter.BuildMultiLineOutput(fields);
            var fieldLines = fieldsText.Split('\n');
            for (int i = 0; i < fieldLines.Length; i++) {
                if (i > 0) {
                    sb.AppendLine();
                }
                sb.Append(AnsiEsc).Append("[K").Append(fieldLines[i].TrimEnd('\r'));
            }

            if (ProgressMode == ProgressMode.Full && additionalLines != null && additionalLines.Count > 0) {
                sb.AppendLine();
                sb.Append(AnsiEsc).Append("[KThreads:");
                foreach (var line in additionalLines) {
                    sb.AppendLine();
                    // Ensure thread line fits within terminal width (accounting for "  " prefix)
                    string formattedLine = line;
                    int maxThreadLineLen = maxLineLength - 2; // "  " prefix
                    if (formattedLine.Length > maxThreadLineLen) {
                        formattedLine = FormatHelpers.TruncateMiddle(formattedLine, maxThreadLineLen);
                    }
                    sb.Append(AnsiEsc).Append("[K  ").Append(formattedLine);
                }
            }
            output = sb.ToString();
        }

        var visualLineCount = CountVisualLines(output, windowWidth);

        lock (_lock) {
            // Move cursor up if not first print
            if (!_firstPrint) {
                Console.Write("\r");
                if (_lastLineCount > 1) {
                    Console.Write(AnsiCursorUpFormat, _lastLineCount - 1);
                }
            }

            // If the new block has fewer lines than before, clear the leftover bottom lines
            if (!_firstPrint && _lastLineCount > visualLineCount) {
                var extraLines = _lastLineCount - visualLineCount;
                var clearSb = new System.Text.StringBuilder(output);
                for (int k = 0; k < extraLines; k++) {
                    clearSb.AppendLine();
                    clearSb.Append(AnsiEsc).Append("[K");
                }
                clearSb.AppendFormat(AnsiCursorUpFormat, extraLines);
                output = clearSb.ToString();
            }

            _firstPrint = false;
            _lastLineCount = visualLineCount;

            // Print output
            Console.Write(output);
        }
    }
}
