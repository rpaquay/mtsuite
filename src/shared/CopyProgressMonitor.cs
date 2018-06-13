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
using mtsuite.shared.Utils;

namespace mtsuite.shared {
  public class CopyProgressMonitor : ProgressMonitor {
    protected override void DisplayStatus(Statistics statistics) {
      var deleteExtraText = new String(' ', 15);
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

      var entriesPerSecondText = string.Format("{0:n0}{1}",
        totalEntriesCount / totalSeconds, deleteExtraText);

      var elapsedTimeText = string.Format("{0}{1}",
        FormatHelpers.FormatElapsedTime(elapsed), deleteExtraText);

      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var copyText = string.Format(
        "{0:n0} ({1:n0} MB)",
        statistics.FileCopiedCount + statistics.SymlinkCopiedCount,
        fileCopiedTotalSizeMb);

      var deleteFilesText = string.Format(
        "{0:n0} ({1:n0} MB)",
        statistics.FileDeletedCount + statistics.SymlinkDeletedCount,
        statistics.FileDeletedTotalSize / 1024 / 1024);

      var deleteDirectoriesText = string.Format(
        "{0:n0}",
        statistics.DirectoryDeletedCount);

      var skippedFilesText = string.Format(
        "{0:n0} ({1:n0} MB)",
        statistics.FileSkippedCount + statistics.SymlinkSkippedCount,
        fileSkippedTotalSizeMb);

      var fields = new[] {
        new KeyValuePair<string, string>("Elapsed time", elapsedTimeText),
        new KeyValuePair<string, string>("Source", ""),
        new KeyValuePair<string, string>("  # of directories", directoriesText),
        new KeyValuePair<string, string>("  # of files", filesText),
        new KeyValuePair<string, string>("Destination", ""),
        new KeyValuePair<string, string>("  # of files copied", copyText),
        new KeyValuePair<string, string>("  # of files skipped", skippedFilesText),
        new KeyValuePair<string, string>("  # of extra directories deleted", deleteDirectoriesText),
        new KeyValuePair<string, string>("  # of extra files deleted", deleteFilesText),
        new KeyValuePair<string, string>("# of entries processed/sec", entriesPerSecondText),
        new KeyValuePair<string, string>("Error count", errorsText),
      };
      Print(fields);
    }
  }
}