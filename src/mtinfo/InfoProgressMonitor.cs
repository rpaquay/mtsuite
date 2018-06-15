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

namespace mtinfo {
  public class InfoProgressMonitor : ProgressMonitor {
    protected override void DisplayStatus(Statistics statistics) {
      var elapsed = statistics.ElapsedTime;
      var totalSeconds = statistics.ElapsedTime.TotalSeconds;

      var directoriesText = string.Format("{0:n0}",
        statistics.DirectoryTraversedCount);

      var filesText = string.Format("{0:n0}",
        statistics.FileProcessedCount + statistics.SymlinkProcessedCount);

      var diskSizeText = string.Format("({0:n0} MB)",
        statistics.FileProcessedTotalSize / 1024 / 1024);

      var statsText = string.Format("{0:n0}",
        statistics.EntryProcessedCount / totalSeconds);

      var elapsedText = string.Format("{0}",
        FormatHelpers.FormatElapsedTime(elapsed));

      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var fields = new[] {
        new PrinterEntry("Elapsed time", elapsedText, valueAlign: Align.Right),
        new PrinterEntry("# of directories", directoriesText, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files", filesText, shortName: "files", valueAlign: Align.Right, extraValue: diskSizeText),
        new PrinterEntry("# of files/sec", statsText, shortName:"files/sec", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName:"errors", valueAlign: Align.Right),
      };
      Print(fields);
    }
  }
}