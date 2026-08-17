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

using System.Collections.Generic;
using mtsuite.shared;
using mtsuite.shared.Utils;

namespace mtdel {
  public class DeleteProgressMonitor : ProgressMonitor<Statistics> {
    protected override void DisplayStatus(Statistics statistics) {
      var elapsedTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.ElapsedTime));
      var cpuTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      var directoriesDeletedText = string.Format("{0:n0}", statistics.DirectoryDeletedCount);
      var filesDeletedText = string.Format("{0:n0}", statistics.FileDeletedCount);
      var filesDeletedSizeText = $"({FormatHelpers.FormatSize(statistics.FileDeletedTotalSize)})";
      var linksDeletedText = string.Format("{0:n0}", statistics.SymlinkDeletedCount);
      var entriesPerSecondText = statistics.ElapsedTime.TotalSeconds > 0 ? string.Format("{0:n0}", statistics.EntryDeletedCount / statistics.ElapsedTime.TotalSeconds) : "0";
      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var fields = new List<PrinterEntry> {
        new PrinterEntry("Elapsed time", elapsedTimeText, valueAlign: Align.Right),
        new PrinterEntry("CPU time", cpuTimeText, valueAlign: Align.Right),
        new PrinterEntry("# of directories deleted", directoriesDeletedText, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files deleted", filesDeletedText, shortName: "files", valueAlign: Align.Right, extraValue: filesDeletedSizeText),
        new PrinterEntry("# of links deleted", linksDeletedText, shortName: "links", valueAlign: Align.Right),
        new PrinterEntry("# of entries deleted/sec", entriesPerSecondText, shortName: "entries/sec", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName: "errors", valueAlign: Align.Right),
      };
      if (ShowGc) {
        fields.Add(GetGcPrinterEntry());
      }
      var threadLines = GetThreadProgressLines();
      Print(fields, threadLines);
    }
  }
}