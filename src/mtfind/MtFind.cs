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
using System.Diagnostics;
using System.Linq;
using System.Reflection;

using mtsuite.CoreFileSystem;
using mtsuite.shared;
using mtsuite.shared.CommandLine;
using mtsuite.shared.Utils;

namespace mtfind {
  public class MtFind {
    private readonly IFileSystem _fileSystem;
    private readonly ParallelFileSystem _parallelFileSystem;
    private readonly FindProgressMonitor _progressMonitor;

    public MtFind(IFileSystem fileSystem) {
      _fileSystem = fileSystem;
      _parallelFileSystem = new ParallelFileSystem(fileSystem);
      _progressMonitor = new FindProgressMonitor();

      _parallelFileSystem.Error += exception => _progressMonitor.OnError(exception);
      _parallelFileSystem.Pulse += () => _progressMonitor.Pulse();

      _parallelFileSystem.EntriesDiscovered += (entry, list) => _progressMonitor.OnEntriesDiscovered(entry, list);
      _parallelFileSystem.DirectoryTraversing += (entry) => _progressMonitor.OnDirectoryTraversing(entry);
      _parallelFileSystem.DirectoryTraversed += (entry) => _progressMonitor.OnDirectoryTraversed(entry);
    }

    public void Run(string[] args) {
      var arguments = new MtFindArguments(args);
      if (!arguments.IsValid || arguments.Values.Help) {
        DisplayBanner();
        if (!arguments.Values.Help) {
          arguments.DisplayArgumentErrors();
        }
        arguments.DisplayUsage();
        throw new CommandLineReturnValueException(16); // To match robocopy
      }

      var sourcePath = ProgramHelpers.MakeFullPath(arguments.Values.Directory);
      var pattern = arguments.Values.Pattern;
      ProgramHelpers.SetWorkerThreadCount(arguments.Values.ThreadCount);
      bool followLinks = !arguments.Values.NoFollowLinks;
      bool isPlainOutput = arguments.Values.PlainOutput;
      if (!isPlainOutput) {
        DisplayBanner();
      }

      var summaryRoot = DoFind(sourcePath, pattern, isPlainOutput, arguments.Values.NoProgress, followLinks);

      DisplayMatchesFiles(summaryRoot, pattern, isPlainOutput);

      var statistics = _progressMonitor.GetStatistics();
      if (!isPlainOutput) {
        DisplayStatistics(statistics);
        Console.WriteLine();
      }

      if (arguments.Values.GarbageCollect) {
        ProgramHelpers.DisplayGcStatistics();
      }

      // 0 = success, 8 = fail (to match robocopy)
      if (statistics.Errors.Count > 0) {
        throw new CommandLineReturnValueException(8);
      }
    }

    private static void DisplayBanner() {
      Console.WriteLine();
      Console.WriteLine("-------------------------------------------------------------------------------");
      Console.WriteLine("MTFIND :: Multi-Threaded File Search for Windows - version {0}",
        Assembly.GetExecutingAssembly().GetName().Version);
      Console.WriteLine("-------------------------------------------------------------------------------");
      Console.WriteLine();
    }

    public DirectorySummaryRoot DoFind(FullPath sourcePath, string pattern, bool isPlainOutput, bool noProgressOutput, bool followLinks) {
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
        Console.WriteLine("Search file names from \"{0}\"", PathHelpers.StripLongPathPrefix(sourcePath.FullName));
        Console.WriteLine();
      }
      _progressMonitor.Start();
      var directorySummaryCollector = new DirectorySummaryCollector(CreateFileNameMatcher(pattern));
      var task = _parallelFileSystem.TraverseDirectoryAsync(sourceDirectory, directorySummaryCollector, followLinks);
      _parallelFileSystem.WaitForTask(task);
      _progressMonitor.Stop();
      return directorySummaryCollector.Root;
    }

    private static FileNameMatcher CreateFileNameMatcher(string pattern) {
      var matcher = new SearchPatternParser().ParsePattern(pattern, SearchPatternParser.Options.Optimize);
      return entry => matcher.MatchString(entry.Path.Name);
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

      ProgramHelpers.DisplayErrors(statistics.Errors);
    }

    private static void DisplayMatchesFiles(DirectorySummaryRoot summaryRoot, string searchPattern, bool isPlainOutput) {
      var directorySummary = summaryRoot.Summary;

      var matchedEntries = EnumerateDirectories(directorySummary)
        .SelectMany(item => item.MatchedFiles)
        .OrderBy(entry => entry.Path)
        .ToList();

      foreach (var entry in matchedEntries) {
        Console.WriteLine(PathHelpers.StripLongPathPrefix(entry.Path.FullName));
      }
      if (!isPlainOutput) {
        Console.WriteLine("Found {0} entries matching pattern \"{1}\"", matchedEntries.Count, searchPattern);
      }
    }

    /// <summary>
    /// Flatten the list of directories starting at <paramref name="root"/>. Returns
    /// an enumeration of (node, depth) tuples.
    /// </summary>
    private static IEnumerable<DirectorySummary> EnumerateDirectories(DirectorySummary root) {
      var stack = new Stack<DirectorySummary>();
      stack.Push(root);
      while (stack.Count > 0) {
        var item = stack.Pop();
        yield return item;
        foreach (var child in item.Children) {
          stack.Push(child);
        }
      }
    }
  }
}
