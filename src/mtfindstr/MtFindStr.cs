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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using mtsuite.CoreFileSystem;
using mtsuite.shared;
using mtsuite.shared.CommandLine;
using mtsuite.shared.FileNameMatching;
using mtsuite.shared.Utils;

namespace mtfindstr {
  public class MtFindStr {
    private readonly IFileSystem _fileSystem;
    private readonly ParallelFileSystem _parallelFileSystem;
    private readonly FindStrProgressMonitor _progressMonitor;

    public MtFindStr(IFileSystem fileSystem) {
      _fileSystem = fileSystem;
      _parallelFileSystem = new ParallelFileSystem(fileSystem);
      _progressMonitor = new FindStrProgressMonitor();

      _parallelFileSystem.Error += (path, exception) => _progressMonitor.OnError(path, exception);
      _parallelFileSystem.Pulse += () => _progressMonitor.Pulse();

      _parallelFileSystem.EntriesDiscovered += (entry, list) => _progressMonitor.OnEntriesDiscovered(entry, list);
      _parallelFileSystem.DirectoryTraversing += (entry) => _progressMonitor.OnDirectoryTraversing(entry);
      _parallelFileSystem.DirectoryTraversed += (entry) => _progressMonitor.OnDirectoryTraversed(entry);
    }

    public void Run(string[] args) {
      var arguments = new MtFindStrArguments(args);
      if (!arguments.IsValid || arguments.Values.Help) {
        DisplayBanner();
        if (!arguments.Values.Help) {
          arguments.DisplayArgumentErrors();
        }
        arguments.DisplayUsage();
        throw new CommandLineReturnValueException(16); // To match robocopy
      }

      var sourcePath = ProgramHelpers.MakeFullPath(arguments.Values.Directory);
      var filePatterns = arguments.Values.FileNamePatterns;
      var searchPattern = arguments.Values.SearchPattern;
      ProgramHelpers.SetWorkerThreadCount(arguments.Values.ThreadCount);
      var followLinks = !arguments.Values.NoFollowLinks;
      var isPlainOutput = arguments.Values.PlainOutput;
      if (!isPlainOutput) {
        DisplayBanner();
      }

      var findStrResult = DoFindStr(sourcePath, filePatterns, searchPattern, isPlainOutput, arguments.Values.NoProgress, followLinks);

      DisplayMatchesFiles(findStrResult, filePatterns, searchPattern, isPlainOutput);

      var statistics = _progressMonitor.GetStatistics();
#if false
      if (!isPlainOutput) {
        DisplayStatistics(statistics);
        Console.WriteLine();
      }
#endif

      if (!isPlainOutput) {
        if (arguments.Values.ShowWarnings) {
          ProgramHelpers.DisplayWarnings(statistics.Warnings);
        }
        ProgramHelpers.DisplayErrors(statistics.Errors);
      }

      if (arguments.Values.GarbageCollect) {
        ProgramHelpers.DisplayGcStatistics(_fileSystem);
      }

      // 0 = success, 8 = fail (to match robocopy)
      if (statistics.Errors.Count > 0) {
        throw new CommandLineReturnValueException(8);
      }
    }

    private static void DisplayBanner() {
      Console.WriteLine();
      Console.WriteLine("-------------------------------------------------------------------------------");
      Console.WriteLine("MTFIND :: Multi-Threaded File String Search for Windows - version {0}",
        Assembly.GetExecutingAssembly().GetName().Version);
      Console.WriteLine("-------------------------------------------------------------------------------");
      Console.WriteLine();
    }

    public List<FindStrFileResult> DoFindStr(FullPath sourcePath, IList<string> fileNamePatterns, string searchPattern, bool isPlainOutput, bool noProgressOutput, bool followLinks) {
      _progressMonitor.QuietMode = isPlainOutput || noProgressOutput;

      // Check source exists
      FileSystemEntry sourceDirectory;
      try {
        sourceDirectory = _fileSystem.GetEntry(sourcePath);
      } catch (Exception e) {
        Console.WriteLine(e.Message);
        // 8 = fail (to match robocopy)
        throw new CommandLineReturnValueException(8);
      }

      if (!isPlainOutput) {
        Console.WriteLine("Searching for string \"{0}\" in files matching pattern(s) {1} under \"{2}\"",
          searchPattern,
          FormatFileNamePatternList(fileNamePatterns),
          PathHelpers.StripLongPathPrefix(sourcePath.FullName));
        Console.WriteLine();
      }
      _progressMonitor.Start();
      var collector = new FindStrSummaryCollector(_progressMonitor, CreateFileNameMatchers(fileNamePatterns), CreateFindStrMatcher(searchPattern));
      var task = _parallelFileSystem.TraverseDirectoryAsync(sourceDirectory, collector, followLinks);
      _parallelFileSystem.WaitForTask(task);
      _progressMonitor.Stop();
      return collector.FileResults;
    }

    private static string FormatFileNamePatternList(IList<string> fileNamePatterns) {
      if (fileNamePatterns == null || fileNamePatterns.Count == 0) {
        return "\"*\"";
      }
      else {
        return fileNamePatterns.Select(x => $"\"{x}\"").Aggregate((a, b) => a + ", " + b);
      }
    }

    private static IList<FileNameMatcher> CreateFileNameMatchers(IList<string> fileNamePatterns) {
      return fileNamePatterns.Select(pattern => {
        var matcher = new SearchPatternParser().ParsePattern(pattern, SearchPatternParser.Options.Optimize);
        return (FileNameMatcher)(entry => matcher.MatchString(entry.Path.Name));
      }).ToList();
    }

    private static FindStrMatcher CreateFindStrMatcher(string pattern) {
      return new FindStrFileEntry(pattern).SearchFile;
    }

    private static void DisplayStatistics(Statistics statistics) {
      var elapsedTimeText = FormatHelpers.FormatElapsedTime(statistics.ElapsedTime);
      var cpuTimeText = FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime);
      var directoriesText = string.Format("{0:n0}", statistics.DirectoryTraversedCount);
      var filesText = string.Format("{0:n0}", statistics.FileEnumeratedCount);
      var symlinksText = string.Format("{0:n0}", statistics.SymlinkEnumeratedCount);
      var entriesPerSecondText = string.Format("{0:n0}", statistics.EntryEnumeratedCount / statistics.ElapsedTime.TotalSeconds);
      var errorsText = string.Format("{0:n0}", statistics.Errors.Count);
      var fields = new[] {
        new PrinterEntry("Statistics"),
        new PrinterEntry("Elapsed time", elapsedTimeText, valueAlign: Align.Right, indent: 2),
        new PrinterEntry("CPU time", cpuTimeText, valueAlign:Align.Right, indent: 2),
        new PrinterEntry("# of directories", directoriesText, shortName: "directories", valueAlign: Align.Right, indent: 2),
        new PrinterEntry("# of files", filesText, shortName: "files", valueAlign: Align.Right, indent: 2),
        new PrinterEntry("# of symlinks", symlinksText, shortName: "symlinks", valueAlign: Align.Right, indent: 2),
        new PrinterEntry("# of entries/sec", entriesPerSecondText, shortName:"entries/sec", valueAlign: Align.Right, indent: 2),
        new PrinterEntry("# of errors", errorsText, shortName:"errors", valueAlign: Align.Right, indent: 2),
      };
      Console.WriteLine();
      FieldsPrinter.WriteLine(fields);
    }

    private static void DisplayMatchesFiles(List<FindStrFileResult> fileResults, IList<string> fileNamePatterns, string searchPattern, bool isPlainOutput) {
      var sortedFileResults = fileResults
        .OrderBy(entry => entry.Path)
        .ToList();

      if (!isPlainOutput) {
        Console.WriteLine("Found {0} occurrences of \"{1}\" in {2} files matching file pattern(s) {3}",
          sortedFileResults.Aggregate(0, (agg, result) => agg + result.Entries.Count),
          searchPattern,
          sortedFileResults.Count,
          FormatFileNamePatternList(fileNamePatterns));
        Console.WriteLine();
      }
      
      foreach (var fileResult in sortedFileResults) {
        foreach (var entry in fileResult.Entries) {
          Console.WriteLine("{0}:{1}:{2}",
            PathHelpers.StripLongPathPrefix(fileResult.Path.FullName),
            entry.LineNumber,
            entry.ColumnNumber);
        }
      }
    }
  }
}
