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
using System.Linq;
using System.Text;

namespace mtsuite.shared {
  public class ProgressPrinter {
    private readonly object _lock = new object();
    private bool _init;
    private int _cursorInitLeft;
    private int _cursorInitTop;
    private bool _supportsPositions;

    public void Stop() {
      Console.WriteLine();
    }

    public void Print(ICollection<PrinterEntry> fields) {
      EnsureInit();
      lock (_lock) {
        if (!_supportsPositions) {
          Console.Write("\r{0}", BuildSingleLineOutput(fields));
        }
        else {
          try {
            Console.SetCursorPosition(_cursorInitLeft, _cursorInitTop);
            Console.Write(BuildMultiLineOutput(fields));
          }
          catch (Exception) {
            Console.Write("\r{0}", BuildSingleLineOutput(fields));
            _supportsPositions = false;
          }
        }
      }
    }

    private static string BuildMultiLineOutput(ICollection<PrinterEntry> fields) {
      var sb = new StringBuilder();
      var nameMaxWidth = fields.Max(kvp => kvp.DisplayName.Length + kvp.Indent);
      var valuesMaxWidth = fields.Max(kvp => kvp.Value?.Length ?? 0);
      var first = true;
      foreach (var field in fields) {
        if (first) {
          first = false;
        }
        else {
          sb.AppendLine();
        }

        var value = field.Value ?? "";
        sb.AppendFormat("{0} {1}{2}{3}         ",
          (new string(' ', field.Indent) + field.DisplayName + ":").PadRight(nameMaxWidth + 2),
          field.ValueAlign == Align.Left ? value.PadRight(valuesMaxWidth) : value.PadLeft(valuesMaxWidth),
          field.ValueUnit == null ? "" : (" " + field.ValueUnit),
          field.ExtraValue == null ? "" : (" " + field.ExtraValue));
      }

      return sb.ToString();
    }

    private static string BuildSingleLineOutput(IEnumerable<PrinterEntry> fields) {
      return fields.Aggregate("", (s, field) => {
        if (field.Value == null) {
          return s;
        }

        var entry = string.Format("{0}: {1}{2}{3}",
          field.ShortName ?? field.DisplayName,
          field.Value,
          field.ValueUnit == null ? "" : (" " + field.ValueUnit),
          field.ExtraValue == null ? "" : (" " + field.ExtraValue));
        if (s.Length > 0) {
          return s + ", " + entry;
        }
        else {
          return s + entry;
        }
      });
    }

    private void EnsureInit() {
      if (_init) {
        return;
      }

      lock (_lock) {
        if (!_init) {
          try {
            _cursorInitLeft = Console.CursorLeft;
            _cursorInitTop = Console.CursorTop;
            _supportsPositions = true;
          }
          catch (Exception) {
            _supportsPositions = false;
          }

          _init = true;
        }
      }
    }
  }
}