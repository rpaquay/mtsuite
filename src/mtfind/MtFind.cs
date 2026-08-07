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

using mtsuite.CoreFileSystem.ObjectPool;

namespace mtfind {
  public class MtFind {
    private readonly IFileSystem _fileSystem;
    private readonly MtPoolFactory _poolFactory;
    private readonly ParallelFileSystem _parallelFileSystem;
    private readonly FindProgressMonitor _progressMonitor;

    public MtFind(IFileSystem fileSystem, MtPoolFactory poolFactory) {
      ArgumentNullException.ThrowIfNull(poolFactory);
      _fileSystem = fileSystem;
      _poolFactory = poolFactory;
      _parallelFileSystem = new ParallelFileSystem(fileSystem, poolFactory);
      _progressMonitor = new FindProgressMonitor();

      _parallelFileSystem.Error += (path, exception) => _progressMonitor.OnError(path, exception);
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
      bool includeDir = arguments.Values.IncludeDir;
      if (!isPlainOutput) {
        DisplayBanner();
      }

      var matchedFiles = DoFind(sourcePath, pattern, isPlainOutput, arguments.Values.NoProgress, followLinks, includeDir);

      DisplayMatchesFiles(matchedFiles, pattern, isPlainOutput);

      var statistics = _progressMonitor.GetStatistics();
#if false
      if (!isPlainOutput) {
        DisplayStatistics(statistics);
        Console.WriteLine();
      }
#endif

      if (arguments.Values.GarbageCollect) {
        ProgramHelpers.DisplayGcStatistics(_poolFactory);
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

    public List<FileSystemEntry> DoFind(FullPath sourcePath, string pattern, bool isPlainOutput, bool noProgressOutput, bool followLinks, bool includeDir) {
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
        Console.WriteLine("Searching files names matching pattern \"{0}\" under \"{1}\"", pattern, sourcePath.FullName);
        Console.WriteLine();
      }
      _progressMonitor.Start();
      var directorySummaryCollector = new DirectorySummaryCollector(_progressMonitor, CreateFileNameMatcher(pattern, includeDir));
      var task = _parallelFileSystem.TraverseDirectoryAsync(sourceDirectory, directorySummaryCollector, followLinks);
      _parallelFileSystem.WaitForTask(task);
      _progressMonitor.Stop();
      return directorySummaryCollector.MatchedFiles;
    }

    private static FileNameMatcher CreateFileNameMatcher(string pattern, bool includeDir) {
      var matcher = new SearchPatternParser().ParsePattern(pattern, SearchPatternParser.Options.Optimize);
      return entry => {
        if (includeDir || !entry.IsDirectory) {
          return matcher.MatchString(entry.Path.Name);
        }
        return false;
      };
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
        new PrinterEntry("# of links", symlinksText, shortName: "links", valueAlign: Align.Right, indent: 2),
        new PrinterEntry("# of entries/sec", entriesPerSecondText, shortName: "entries/sec", valueAlign: Align.Right, indent: 2),
        new PrinterEntry("# of errors", errorsText, shortName: "errors", valueAlign: Align.Right, indent: 2),
      };
      Console.WriteLine();
      FieldsPrinter.WriteLine(fields);

      ProgramHelpers.DisplayErrors(statistics.Errors);
    }

    private static void DisplayMatchesFiles(List<FileSystemEntry> matchedFiles, string searchPattern, bool isPlainOutput) {
      var matchedEntries = matchedFiles
        .OrderBy(entry => entry.Path)
        .ToList();

      foreach (var entry in matchedEntries) {
        Console.WriteLine(entry.Path.FullName);
      }
      if (!isPlainOutput) {
        Console.WriteLine();
        Console.WriteLine("Found {0} file names matching pattern \"{1}\"", matchedEntries.Count, searchPattern);
      }
    }
  }
}
