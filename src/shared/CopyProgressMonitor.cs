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

      var directoriesText = string.Format("{0:n0}",
        statistics.DirectoryEnumeratedCount);

      var filesText = string.Format("{0:n0}",
        statistics.FileEnumeratedCount);

      var diskSizeText = string.Format("({0:n0} MB)",
        statistics.FileEnumeratedTotalSize / 1024 / 1024);

      var entriesPerSecondText = string.Format("{0:n0}",
        totalEntriesCount / totalSeconds);

      var elapsedTimeText = string.Format("{0}",
        FormatHelpers.FormatElapsedTime(elapsed));

      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var copyText = string.Format(
        "{0:n0}",
        statistics.FileCopiedCount + statistics.SymlinkCopiedCount);

      var copyExtraText = string.Format(
        "({0:n0} MB)",
        fileCopiedTotalSizeMb);

      var deleteFilesText = string.Format(
        "{0:n0}",
        statistics.FileDeletedCount + statistics.SymlinkDeletedCount);

      var deleteExtraText = string.Format(
        "({0:n0} MB)",
        statistics.FileDeletedTotalSize / 1024 / 1024);

      var deleteDirectoriesText = string.Format(
        "{0:n0}",
        statistics.DirectoryDeletedCount);

      var skippedFilesText = string.Format(
        "{0:n0}",
        statistics.FileSkippedCount + statistics.SymlinkSkippedCount);

      var skippedExtraText = string.Format(
        "({0:n0} MB)",
        fileSkippedTotalSizeMb);

      var fields = new[] {
        new PrinterEntry("Elapsed time", elapsedTimeText),
        new PrinterEntry("Source", null),
        new PrinterEntry("# of directories", directoriesText, indent: 2, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files", filesText, indent: 2, shortName: "files", valueAlign: Align.Right, extraValue: diskSizeText),
        new PrinterEntry("Destination", null),
        new PrinterEntry("# of files copied", copyText, indent: 2, shortName: "copied", valueAlign: Align.Right, extraValue: copyExtraText),
        new PrinterEntry("# of files skipped", skippedFilesText, indent: 2, shortName: "skipped", valueAlign: Align.Right, extraValue: skippedExtraText),
        new PrinterEntry("# of extra directories deleted", deleteDirectoriesText, indent: 2, shortName: "directories deleted", valueAlign: Align.Right),
        new PrinterEntry("# of extra files deleted", deleteFilesText, indent: 2, shortName: "files deleted", valueAlign: Align.Right, extraValue: deleteExtraText),
        new PrinterEntry("# of entries processed/sec", entriesPerSecondText, shortName: "files/sec", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName: "errors", valueAlign: Align.Right),
      };
      Print(fields);
    }
  }
}