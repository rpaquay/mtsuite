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
using System.ComponentModel;
using mtsuite.CoreFileSystem.Win32;
using mtsuite.shared;
using mtsuite.shared.Utils;

namespace mtfind {
  public class FindProgressMonitor : ProgressMonitor {
    protected override void DisplayStatus(Statistics statistics) {
      var elapsedTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.ElapsedTime));
      var cpuTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      var directoriesText = string.Format("{0:n0}", statistics.DirectoryTraversedCount);
      var filesText = string.Format("{0:n0}", statistics.EntryEnumeratedCount);
      var entriesPerSecondText = string.Format("{0:n0}", statistics.EntryEnumeratedCount / statistics.ElapsedTime.TotalSeconds);
      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var fields = new[] {
        new PrinterEntry("Elapsed time", elapsedTimeText, valueAlign: Align.Right),
        new PrinterEntry("CPU time", cpuTimeText, valueAlign:Align.Right),
        new PrinterEntry("# of directories", directoriesText, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files", filesText, shortName: "files", valueAlign: Align.Right),
        new PrinterEntry("# of files/sec", entriesPerSecondText, shortName:"files/sec", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName:"errors", valueAlign: Align.Right),
      };
      Print(fields);
    }

    /// <summary>
    /// Ignore errors that are harmless, such as inability to enumerate files in
    /// a directory.
    /// </summary>
    public override void OnError(Exception e) {
      if (IsIgnorableError(e)) {
        return;
      }

      base.OnError(e);
    }

    private bool IsIgnorableError(Exception e) {
      var win32Error = e as Win32Exception;
      if (win32Error == null) {
        return false;
      }

      switch (win32Error.NativeErrorCode) {
        case (int)Win32Errors.ERROR_PATH_NOT_FOUND:
          return true;
        default:
          return false;
      }
    }
  }
}