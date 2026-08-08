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

using System;
using System.IO;
using System.Threading;
using mtsuite.CoreFileSystem;
using mtsuite.shared;
using mtsuite.shared.Utils;

namespace mtfind {
  public class FindProgressMonitor : ProgressMonitor<FindStatistics> {
    private long _fileMatchedCount;

    public bool QuietMode { get; set; }

    protected override void FillInStatistics(FindStatistics statistics) {
      base.FillInStatistics(statistics);
      statistics.FileMatchedCount = _fileMatchedCount;
    }

    protected override void DisplayStatus(FindStatistics statistics) {
      if (QuietMode) {
        return;
      }

      var elapsedTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.ElapsedTime));
      var cpuTimeText = string.Format("{0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      var directoriesText = string.Format("{0:n0}", statistics.DirectoryTraversedCount);
      var filesText = string.Format("{0:n0}", statistics.FileEnumeratedCount);
      var linksText = string.Format("{0:n0}", statistics.SymlinkEnumeratedCount);
      var filesMatchedCount = string.Format("{0:n0}", statistics.FileMatchedCount);
      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);

      var fields = new[] {
        new PrinterEntry("Elapsed time", elapsedTimeText, valueAlign: Align.Right),
        new PrinterEntry("CPU time", cpuTimeText, valueAlign: Align.Right),
        new PrinterEntry("# of directories", directoriesText, shortName: "directories", valueAlign: Align.Right),
        new PrinterEntry("# of files", filesText, shortName: "files", valueAlign: Align.Right),
        new PrinterEntry("# of links", linksText, shortName: "links", valueAlign: Align.Right),
        new PrinterEntry("# of files matching pattern", filesMatchedCount, shortName: "matched", valueAlign: Align.Right),
        new PrinterEntry("# of errors", errorsText, shortName: "errors", valueAlign: Align.Right),
      };
      var threadLines = GetThreadProgressLines();
      Print(fields, threadLines);
    }

    public void OnFileMatchFound(FileSystemEntry entry) {
      Interlocked.Increment(ref _fileMatchedCount);
      if (QuietMode) {
        return;
      }

      PrintMessage(() => {
        Console.WriteLine(entry.Path.FullName);
      });
    }

    public void OnFileMatchFound() {
      Interlocked.Increment(ref _fileMatchedCount);
    }

    /// <summary>
    /// Ignore errors that are harmless, such as inability to enumerate files in
    /// a directory.
    /// </summary>
    public override bool IsWarning(FullPath path, Exception e) {
      return IsIgnorableError(e);
    }

    private static bool IsIgnorableError(Exception e) {
      return e is DirectoryNotFoundException || e is FileNotFoundException;
    }
  }
}