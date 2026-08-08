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
  public class CopyProgressMonitor : ProgressMonitor<Statistics> {
    protected override void DisplayStatus(Statistics statistics) {
      var elapsed = statistics.ElapsedTime;
      var totalSeconds = elapsed.TotalSeconds;
      var isCloning = statistics.FileClonedCount > 0 || (statistics.FileCopiedCount == 0 && statistics.FileClonedTotalSize > 0);
      var totalEntriesCount =
        statistics.DirectoryTraversedCount + statistics.FileCopiedCount + statistics.FileClonedCount + statistics.SymlinkCopiedCount +
        statistics.DirectoryDeletedCount + statistics.FileDeletedCount + statistics.SymlinkDeletedCount +
        statistics.FileSkippedCount;

      var elapsedTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(elapsed));
      var cpuTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      var sourceDirectoriesText = string.Format("{0:n0}", statistics.DirectoryEnumeratedCount);
      var sourceFilesText = string.Format("{0:n0}", statistics.FileEnumeratedCount);
      var sourceFilesExtraText = $"({FormatHelpers.FormatSize(statistics.FileEnumeratedTotalSize)})";
      var sourceLinksText = string.Format("{0:n0}", statistics.SymlinkEnumeratedCount);
      var filesCopiedOrClonedText = isCloning
        ? string.Format("{0:n0}", statistics.FileClonedCount)
        : string.Format("{0:n0}", statistics.FileCopiedCount);
      var filesCopiedOrClonedExtraText = isCloning
        ? $"({FormatHelpers.FormatSize(statistics.FileClonedTotalSize)})"
        : $"({FormatHelpers.FormatSize(statistics.FileCopiedTotalSize)})";
      var linksCopiedText = string.Format("{0:n0}", statistics.SymlinkCopiedCount);
      var filesSkippedText = string.Format("{0:n0}", statistics.FileSkippedCount);
      var filesSkippedExtraText = $"({FormatHelpers.FormatSize(statistics.FileSkippedTotalSize)})";
      var linksSkippedText = string.Format("{0:n0}", statistics.SymlinkSkippedCount);
      var directoriesDeletedText = string.Format("{0:n0}", statistics.DirectoryDeletedCount);
      var filesDeletedText = string.Format("{0:n0}", statistics.FileDeletedCount);
      var filesDeletedExtraText = $"({FormatHelpers.FormatSize(statistics.FileDeletedTotalSize)})";
      var linksDeletedText = string.Format("{0:n0}", statistics.SymlinkDeletedCount);
      var entriesPerSecondText = totalSeconds > 0 ? string.Format("{0:n0}", totalEntriesCount / totalSeconds) : "0";
      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var copiedOrClonedLabel = isCloning ? "# of files cloned" : "# of files copied";
      var copiedOrClonedShort = isCloning ? "cloned" : "copied";

      var fields = new[] {
        new PrinterEntry("Elapsed time", elapsedTimeText),
        new PrinterEntry("CPU time", cpuTimeText, valueAlign: Align.Right),
        new PrinterEntry("Source"),
        new PrinterEntry("# of directories", sourceDirectoriesText, indent: 2, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files", sourceFilesText, indent: 2, shortName: "files", valueAlign: Align.Right, extraValue: sourceFilesExtraText),
        new PrinterEntry("# of links", sourceLinksText, indent: 2, shortName: "links", valueAlign: Align.Right),
        new PrinterEntry("Destination"),
        new PrinterEntry(copiedOrClonedLabel, filesCopiedOrClonedText, indent: 2, shortName: copiedOrClonedShort, valueAlign: Align.Right, extraValue: filesCopiedOrClonedExtraText),
        new PrinterEntry("# of links copied", linksCopiedText, indent: 2, shortName: "links copied", valueAlign: Align.Right),
        new PrinterEntry("# of files skipped", filesSkippedText, indent: 2, shortName: "skipped", valueAlign: Align.Right, extraValue: filesSkippedExtraText),
        new PrinterEntry("# of links skipped", linksSkippedText, indent: 2, shortName: "links skipped", valueAlign: Align.Right),
        new PrinterEntry("# of extra directories deleted", directoriesDeletedText, indent: 2, shortName: "directories deleted", valueAlign: Align.Right),
        new PrinterEntry("# of extra files deleted", filesDeletedText, indent: 2, shortName: "files deleted", valueAlign: Align.Right, extraValue: filesDeletedExtraText),
        new PrinterEntry("# of extra links deleted", linksDeletedText, indent: 2, shortName: "links deleted", valueAlign: Align.Right),
        new PrinterEntry("# of entries processed/sec", entriesPerSecondText, shortName: "files/sec", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName: "errors", valueAlign: Align.Right),
      };
      var threadLines = GetThreadProgressLines();
      Print(fields, threadLines);
    }
  }
}