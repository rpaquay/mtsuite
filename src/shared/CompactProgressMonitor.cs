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

using mtsuite.shared.Utils;

namespace mtsuite.shared {
  public class CompactProgressMonitor : ProgressMonitor<Statistics> {
    public bool IsDryRun { get; set; }

    protected override void DisplayStatus(Statistics statistics) {
      var elapsed = statistics.ElapsedTime;
      var totalSeconds = elapsed.TotalSeconds;
      var fileCloneTotalSizeMb = statistics.FileClonedTotalSize / 1024 / 1024;
      var fileSkippedTotalSizeMb = statistics.FileCloneSkippedTotalSize / 1024 / 1024;
      var totalEntriesCount = statistics.DirectoryTraversedCount + statistics.FileClonedCount + statistics.FileCloneSkippedCount;

      var elapsedTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(elapsed));
      var cpuTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      var sourceDirectoriesText = string.Format("{0:n0}", statistics.DirectoryEnumeratedCount);
      var sourceFilesText = string.Format("{0:n0}", statistics.EntryEnumeratedCount);
      var sourceFilesExtraText = string.Format("({0:n0} MB)", statistics.FileEnumeratedTotalSize / 1024 / 1024);
      var filesCompactedText = string.Format("{0:n0}", statistics.FileClonedCount);
      var filesCompactedExtraText = string.Format("({0:n0} MB)", fileCloneTotalSizeMb);
      var filesSkippedText = string.Format("{0:n0}", statistics.FileCloneSkippedCount);
      var filesSkippedExtraText = string.Format("({0:n0} MB)", fileSkippedTotalSizeMb);
      var entriesPerSecondText = totalSeconds > 0 ? string.Format("{0:n0}", totalEntriesCount / totalSeconds) : "0";
      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var compactedLabel = IsDryRun ? "# of files to compact" : "# of files compacted";
      var compactedShort = IsDryRun ? "to-compact" : "compacted";

      var fields = new[] {
        new PrinterEntry("Elapsed time", elapsedTimeText),
        new PrinterEntry("CPU time", cpuTimeText, valueAlign: Align.Right),
        new PrinterEntry("Source"),
        new PrinterEntry("# of directories", sourceDirectoriesText, indent: 2, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files", sourceFilesText, indent: 2, shortName: "files", valueAlign: Align.Right, extraValue: sourceFilesExtraText),
        new PrinterEntry("Destination"),
        new PrinterEntry(compactedLabel, filesCompactedText, indent: 2, shortName: compactedShort, valueAlign: Align.Right, extraValue: filesCompactedExtraText),
        new PrinterEntry("# of files skipped", filesSkippedText, indent: 2, shortName: "skipped", valueAlign: Align.Right, extraValue: filesSkippedExtraText),
        new PrinterEntry("# of entries processed/sec", entriesPerSecondText, shortName: "files/sec", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName: "errors", valueAlign: Align.Right),
      };
      var threadLines = GetThreadProgressLines();
      Print(fields, threadLines);
    }
  }
}
