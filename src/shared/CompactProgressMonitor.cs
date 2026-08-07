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
      var totalEntriesCount = statistics.DirectoryTraversedCount + statistics.FileClonedCount + statistics.FileAlreadyClonedCount + statistics.FileCloneSkippedCount;

      var elapsedTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(elapsed));
      var cpuTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      var sourceDirectoriesText = string.Format("{0:n0}", statistics.DirectoryEnumeratedCount);
      var sourceFilesText = string.Format("{0:n0}", statistics.FileEnumeratedCount);
      var sourceFilesExtraText = $"({FormatHelpers.FormatSize(statistics.FileEnumeratedTotalSize)})";
      var sourceLinksText = string.Format("{0:n0}", statistics.SymlinkEnumeratedCount);
      var filesClonedText = string.Format("{0:n0}", statistics.FileClonedCount);
      var filesClonedExtraText = $"({FormatHelpers.FormatSize(statistics.FileClonedTotalSize)})";
      var filesAlreadyClonedText = string.Format("{0:n0}", statistics.FileAlreadyClonedCount);
      var filesAlreadyClonedExtraText = $"({FormatHelpers.FormatSize(statistics.FileAlreadyClonedTotalSize)})";
      var filesSkippedText = string.Format("{0:n0}", statistics.FileCloneSkippedCount);
      var filesSkippedExtraText = $"({FormatHelpers.FormatSize(statistics.FileCloneSkippedTotalSize)})";
      var entriesPerSecondText = totalSeconds > 0 ? string.Format("{0:n0}", totalEntriesCount / totalSeconds) : "0";
      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var clonedLabel = IsDryRun ? "# of files to clone" : "# of files cloned";
      var clonedShort = IsDryRun ? "to-clone" : "cloned";

      var fields = new[] {
        new PrinterEntry("Elapsed time", elapsedTimeText),
        new PrinterEntry("CPU time", cpuTimeText, valueAlign: Align.Right),
        new PrinterEntry("Source"),
        new PrinterEntry("# of directories", sourceDirectoriesText, indent: 2, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files", sourceFilesText, indent: 2, shortName: "files", valueAlign: Align.Right, extraValue: sourceFilesExtraText),
        new PrinterEntry("# of links", sourceLinksText, indent: 2, shortName: "links", valueAlign: Align.Right),
        new PrinterEntry("Destination"),
        new PrinterEntry(clonedLabel, filesClonedText, indent: 2, shortName: clonedShort, valueAlign: Align.Right, extraValue: filesClonedExtraText),
        new PrinterEntry("# of files already cloned", filesAlreadyClonedText, indent: 2, shortName: "already", valueAlign: Align.Right, extraValue: filesAlreadyClonedExtraText),
        new PrinterEntry("# of files skipped", filesSkippedText, indent: 2, shortName: "skipped", valueAlign: Align.Right, extraValue: filesSkippedExtraText),
        new PrinterEntry("# of entries processed/sec", entriesPerSecondText, shortName: "files/sec", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName: "errors", valueAlign: Align.Right),
      };
      var threadLines = GetThreadProgressLines();
      Print(fields, threadLines);
    }
  }
}
