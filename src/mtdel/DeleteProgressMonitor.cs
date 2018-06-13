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

using System.Collections.Generic;
using mtsuite.shared;
using mtsuite.shared.Utils;

namespace mtdel {
  public class DeleteProgressMonitor : ProgressMonitor {
    protected override void DisplayStatus(Statistics statistics) {
      var deleteExtraText = new string(' ', 15);
      var elapsed = statistics.ElapsedTime;
      var totalSeconds = elapsed.TotalSeconds;

      var directoriesDeletedText = string.Format("{0:n0}",
        statistics.DirectoryDeletedCount);

      var filesDeletedText = string.Format("{0:n0} ({1:n0} MB)",
        statistics.FileDeletedCount + statistics.SymlinkDeletedCount,
        statistics.FileDeletedTotalSize / 1024 / 1024);

      var deletedPerSecondText = string.Format("{0:n0}{1}",
        (statistics.FileDeletedCount + statistics.SymlinkDeletedCount) / totalSeconds,
        deleteExtraText);

      var elapsedText = string.Format("{0}{1}",
        FormatHelpers.FormatElapsedTime(elapsed),
        deleteExtraText);

      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var fields = new KeyValuePair<string, string>[] {
        new KeyValuePair<string, string>("Elapsed time", elapsedText),
        new KeyValuePair<string, string>("# of directories deleted", directoriesDeletedText),
        new KeyValuePair<string, string>("# of files deleted", filesDeletedText),
        new KeyValuePair<string, string>("# of files deleted/sec", deletedPerSecondText),
        new KeyValuePair<string, string>("# of errors", errorsText),
      };
      Print(fields);
    }
  }
}