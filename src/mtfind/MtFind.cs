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
      var argumentDefinitions = new ArgumentDefinitionBuilder()
        .WithHelpSwitch()
        .WithThreadCountSwitch()
        .WithGcSwitch()
        .WithStringFlag("path", "The directory tree to search", "p", "path", Environment.CurrentDirectory, null, "", "path")
        .WithSwitch("plain-output", "Plain output, i.e. only display list of file paths that match the search pattern", "plain")
        .WithString("pattern", "The pattern to search for", true, "*")
        .Build();

      var parser = new ArgumentsParser(argumentDefinitions, args);
      parser.Parse();
      if (!parser.IsValid || parser.Contains("help")) {
        DisplayBanner();
        if (!parser.Contains("help")) {
          foreach (var error in parser.Errors) {
            Console.WriteLine("ERROR: {0}", error);
          }
          Console.WriteLine();
        }
        DisplayUsage(argumentDefinitions);
        throw new CommandLineReturnValueException(16); // To match robocopy
      }

      var sourcePath = ProgramHelpers.MakeFullPath(parser["path"].StringValue);
      var pattern = parser["pattern"].StringValue;
      ProgramHelpers.SetWorkerThreadCount(parser["thread-count"].IntValue);
      bool isPlainOutput = parser.Contains("plain-output");
      if (!isPlainOutput) {
        DisplayBanner();
      }

      var summaryRoot = DoFind(sourcePath, pattern, isPlainOutput);

      var statistics = _progressMonitor.GetStatistics();
      if (!isPlainOutput) {
        DisplayResults(statistics);
        Console.WriteLine();
      }

      DisplayMatchesFiles(summaryRoot, pattern, isPlainOutput);

      if (parser.Contains("gc")) {
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

    private static void DisplayUsage(IList<ArgDef> argumentDefinitions) {
      Console.WriteLine("Search for file names inside a directory.");
      Console.WriteLine();
      Console.WriteLine("Usage: {0} {1}", Process.GetCurrentProcess().ProcessName,
        ArgumentsHelper.BuildUsageSummary(argumentDefinitions));
      Console.WriteLine();
      ArgumentsHelper.PrintArgumentUsageSummary(argumentDefinitions);
    }

    public DirectorySummaryRoot DoFind(FullPath sourcePath, string pattern, bool isPlainOutput) {
      _progressMonitor.QuietMode = isPlainOutput;

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
      }
      _progressMonitor.Start();
      var directorySummaryCollector = new DirectorySummaryCollector(CreateFileNameMatcher(pattern));
      var task = _parallelFileSystem.TraverseDirectoryAsync(sourceDirectory, directorySummaryCollector, true);
      _parallelFileSystem.WaitForTask(task);
      _progressMonitor.Stop();
      return directorySummaryCollector.Root;
    }

    private static FileNameMatcher CreateFileNameMatcher(string pattern) {
      var matcher = new SearchPatternParser().ParsePattern(pattern, SearchPatternParser.Options.Optimize);
      return entry => matcher.MatchString(entry.Path.Name);
    }

    private static void DisplayResults(Statistics statistics) {
      Console.WriteLine();
      Console.WriteLine("Statistics:");
      Console.WriteLine("  Elapsed time:             {0}", FormatHelpers.FormatElapsedTime(statistics.ElapsedTime));
      Console.WriteLine("  CPU time:                 {0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      Console.WriteLine("  # of directories:         {0:n0}", statistics.DirectoryTraversedCount);
      Console.WriteLine("  # of files:               {0:n0}", statistics.FileEnumeratedCount);
      Console.WriteLine("  # of symlinks:            {0:n0}", statistics.SymlinkEnumeratedCount);
      Console.WriteLine("  # entries/sec:            {0:n0}",
        statistics.EntryEnumeratedCount / statistics.ElapsedTime.TotalSeconds);

      Console.WriteLine("  # of errors:              {0:n0}", statistics.Errors.Count);
      ProgramHelpers.DisplayErrors(statistics.Errors);
    }

    private static void DisplayMatchesFiles(DirectorySummaryRoot summaryRoot, string searchPattern, bool isPlainOutput) {
      var directorySummary = summaryRoot.Summary;

      var matchedEntries = EnumerateDirectories(directorySummary)
        .SelectMany(item => item.MatchedFiles)
        .OrderBy(entry => entry.Path)
        .ToList();

      if (!isPlainOutput) {
        Console.WriteLine("Found {0} entries matching pattern \"{1}\"", matchedEntries.Count, searchPattern);
      }
      foreach (var entry in matchedEntries) {
        Console.WriteLine(PathHelpers.StripLongPathPrefix(entry.Path.FullName));
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
