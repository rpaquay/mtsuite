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

using mtsuite.shared;
using mtsuite.shared.Utils;

namespace mtinfo {
  public class InfoProgressMonitor : ProgressMonitor<Statistics> {
    protected override void DisplayStatus(Statistics statistics) {
      var elapsedTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.ElapsedTime));
      var cpuTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      var directoriesText = string.Format("{0:n0}", statistics.DirectoryTraversedCount);
      var filesText = string.Format("{0:n0}", statistics.FileEnumeratedCount);
      var filesExtraText = $"({FormatHelpers.FormatSize(statistics.FileEnumeratedTotalSize)})";
      var linksText = string.Format("{0:n0}", statistics.SymlinkEnumeratedCount);
      var entriesPerSecondText = statistics.ElapsedTime.TotalSeconds > 0 ? string.Format("{0:n0}", statistics.EntryEnumeratedCount / statistics.ElapsedTime.TotalSeconds) : "0";
      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var fields = new[] {
        new PrinterEntry("Elapsed time", elapsedTimeText, valueAlign: Align.Right),
        new PrinterEntry("CPU time", cpuTimeText, valueAlign: Align.Right),
        new PrinterEntry("# of directories", directoriesText, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files", filesText, shortName: "files", valueAlign: Align.Right, extraValue: filesExtraText),
        new PrinterEntry("# of links", linksText, shortName: "links", valueAlign: Align.Right),
        new PrinterEntry("# of entries/sec", entriesPerSecondText, shortName: "entries/sec", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName: "errors", valueAlign: Align.Right),
      };
      var threadLines = GetThreadProgressLines();
      Print(fields, threadLines);
    }
  }
}