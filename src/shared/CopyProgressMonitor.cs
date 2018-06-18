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

using mtsuite.shared.Utils;

namespace mtsuite.shared {
  public class CopyProgressMonitor : ProgressMonitor {
    protected override void DisplayStatus(Statistics statistics) {
      var elapsed = statistics.ElapsedTime;
      var totalSeconds = elapsed.TotalSeconds;
      var fileCopiedTotalSizeMb = statistics.FileCopiedTotalSize / 1024 / 1024;
      var fileSkippedTotalSizeMb = statistics.FileSkippedTotalSize / 1024 / 1024;
      var totalEntriesCount =
        statistics.DirectoryTraversedCount + statistics.FileCopiedCount + statistics.SymlinkCopiedCount +
        statistics.DirectoryDeletedCount + statistics.FileDeletedCount + statistics.SymlinkDeletedCount +
        statistics.FileSkippedCount;

      var elapsedTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(elapsed));
      var cpuTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      var sourceDirectoriesText = string.Format("{0:n0}", statistics.DirectoryEnumeratedCount);
      var sourceFilesText = string.Format("{0:n0}", statistics.EntryEnumeratedCount);
      var sourceFilesExtraText = string.Format("({0:n0} MB)", statistics.FileEnumeratedTotalSize / 1024 / 1024);
      var filesCopiedText = string.Format("{0:n0}", statistics.FileCopiedCount + statistics.SymlinkCopiedCount);
      var filesCopiedExtraText = string.Format("({0:n0} MB)", fileCopiedTotalSizeMb);
      var filesSkippedText = string.Format("{0:n0}", statistics.FileSkippedCount + statistics.SymlinkSkippedCount);
      var filesSkippedExtraText = string.Format("({0:n0} MB)", fileSkippedTotalSizeMb);
      var directoriesDeletedText = string.Format("{0:n0}", statistics.DirectoryDeletedCount);
      var filesDeletedText = string.Format("{0:n0}", statistics.FileDeletedCount + statistics.SymlinkDeletedCount);
      var filesDeletedExtraText = string.Format("({0:n0} MB)", statistics.FileDeletedTotalSize / 1024 / 1024);
      var entriesPerSecondText = string.Format("{0:n0}", totalEntriesCount / totalSeconds);
      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var fields = new[] {
        new PrinterEntry("Elapsed time", elapsedTimeText),
        new PrinterEntry("CPU time", cpuTimeText, valueAlign:Align.Right),
        new PrinterEntry("Source"),
        new PrinterEntry("# of directories", sourceDirectoriesText, indent: 2, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files", sourceFilesText, indent: 2, shortName: "files", valueAlign: Align.Right, extraValue: sourceFilesExtraText),
        new PrinterEntry("Destination"),
        new PrinterEntry("# of files copied", filesCopiedText, indent: 2, shortName: "copied", valueAlign: Align.Right, extraValue: filesCopiedExtraText),
        new PrinterEntry("# of files skipped", filesSkippedText, indent: 2, shortName: "skipped", valueAlign: Align.Right, extraValue: filesSkippedExtraText),
        new PrinterEntry("# of extra directories deleted", directoriesDeletedText, indent: 2, shortName: "directories deleted", valueAlign: Align.Right),
        new PrinterEntry("# of extra files deleted", filesDeletedText, indent: 2, shortName: "files deleted", valueAlign: Align.Right, extraValue: filesDeletedExtraText),
        new PrinterEntry("# of entries processed/sec", entriesPerSecondText, shortName: "files/sec", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName: "errors", valueAlign: Align.Right),
      };
      Print(fields);
    }
  }
}