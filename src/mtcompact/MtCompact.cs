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
using System.Reflection;
using mtsuite.CoreFileSystem;
using mtsuite.shared;
using mtsuite.shared.CommandLine;
using mtsuite.shared.Utils;

namespace mtcompact {
  public class MtCompact {
    private readonly IFileSystem _fileSystem;
    private readonly ParallelFileSystem _parallelFileSystem;
    private readonly CompactProgressMonitor _progressMonitor;

    public MtCompact(IFileSystem fileSystem) {
      _fileSystem = fileSystem;
      _parallelFileSystem = new ParallelFileSystem(fileSystem);
      _progressMonitor = new CompactProgressMonitor();

      _parallelFileSystem.Error += (path, exception) => _progressMonitor.OnError(path, exception);
      _parallelFileSystem.Pulse += () => _progressMonitor.Pulse();

      _parallelFileSystem.FileCompacting += (entry) => _progressMonitor.OnFileCompacting(entry);
      _parallelFileSystem.FileCompacted += (entry, elapsed, bytes) => _progressMonitor.OnFileCompacted(entry, elapsed, bytes);
      _parallelFileSystem.FileCompactSkipped += (entry) => _progressMonitor.OnFileCompactSkipped(entry, entry.FileSize);

      _parallelFileSystem.DirectoryTraversing += (entry) => _progressMonitor.OnDirectoryTraversing(entry);
      _parallelFileSystem.DirectoryTraversed += (entry) => _progressMonitor.OnDirectoryTraversed(entry);
      _parallelFileSystem.EntriesDiscovered += (entry, list) => _progressMonitor.OnEntriesDiscovered(entry, list);
    }

    public void Run(string[] args) {
      DisplayBanner();

      var argumentDefinitions = new ArgumentDefinitionBuilder()
        .WithString("source-path", "The path of the source directory", true)
        .WithString("destination-path", "The path of the destination directory", true)
        .WithSwitch("fc", "Compare file contents (default)", "fc", "", "content")
        .WithSwitch("ft", "Fast comparison using file modification time only", "ft")
        .WithSwitch("dry-run", "Simulate compaction without modifying files to compute potential space savings", "dry-run", "n")
        .WithThreadCountSwitch()
        .WithGcSwitch()
        .WithHelpSwitch()
        .Build();

      var parser = new ArgumentsParser(argumentDefinitions, args);
      parser.Parse();
      if (!parser.IsValid || parser.Contains("help")) {
        if (!parser.Contains("help")) {
          foreach (var error in parser.Errors) {
            Console.WriteLine("ERROR: {0}", error);
          }
          Console.WriteLine();
        }
        DisplayUsage(argumentDefinitions);
        throw new CommandLineReturnValueException(16);
      }

      var sourcePath = ProgramHelpers.MakeFullPath(parser["source-path"].StringValue);
      var destinationPath = ProgramHelpers.MakeFullPath(parser["destination-path"].StringValue);
      ProgramHelpers.SetWorkerThreadCount(parser["thread-count"].IntValue);
      IFileComparer fileComparer;
      if (parser.Contains("ft")) {
        fileComparer = new LastWriteTimeFileComparer(_fileSystem);
      } else {
        fileComparer = new FileContentsFileComparer(_fileSystem);
      }

      var explicitDryRun = parser.Contains("dry-run");
      var supportsCloning = _fileSystem.SupportsCloning(sourcePath, destinationPath);
      var isDryRun = explicitDryRun || !supportsCloning;

      if (!supportsCloning) {
        Console.WriteLine("NOTICE: File cloning is not supported on this platform/filesystem.");
        Console.WriteLine("Running in SIMULATION mode to compute potential space savings.");
        Console.WriteLine();
      } else if (explicitDryRun) {
        Console.WriteLine("NOTICE: Running in SIMULATION mode (--dry-run). No files will be modified.");
        Console.WriteLine();
      }

      var statistics = DoCompact(sourcePath, destinationPath, fileComparer, isDryRun);
      DisplayResults(statistics, isDryRun);
      if (parser.Contains("gc")) {
        ProgramHelpers.DisplayGcStatistics(_fileSystem);
      }

      if (statistics.Errors.Count > 0)
        throw new CommandLineReturnValueException(8);
    }

    private static void DisplayUsage(IList<ArgDef> argumentDefinitions) {
      Console.WriteLine("Compacts identical files between source and destination directories using Copy-on-Write (cloning).");
      Console.WriteLine("If file cloning is not supported on the host OS or filesystem, automatically runs in simulation mode.");
      Console.WriteLine();
      Console.WriteLine("Usage: {0} {1}", Process.GetCurrentProcess().ProcessName,
        ArgumentsHelper.BuildUsageSummary(argumentDefinitions));
      Console.WriteLine();
      ArgumentsHelper.PrintArgumentUsageSummary(argumentDefinitions);
    }

    public Statistics DoCompact(FullPath sourcePath, FullPath destinationPath, IFileComparer fileComparer, bool isDryRun = false) {
      FileSystemEntry sourceDirectory;
      try {
        sourceDirectory = _fileSystem.GetEntry(sourcePath);
      } catch (Exception e) {
        Console.WriteLine(e.Message);
        throw new CommandLineReturnValueException(8);
      }

      if (isDryRun) {
        Console.WriteLine("Analyzing identical files between \"{0}\" and \"{1}\"",
          PathHelpers.StripLongPathPrefix(sourcePath.FullName), PathHelpers.StripLongPathPrefix(destinationPath.FullName));
      } else {
        Console.WriteLine("Compacting identical files from \"{0}\" to \"{1}\"",
          PathHelpers.StripLongPathPrefix(sourcePath.FullName), PathHelpers.StripLongPathPrefix(destinationPath.FullName));
      }

      _progressMonitor.SourcePath = sourcePath;
      _progressMonitor.DestinationPath = destinationPath;
      _progressMonitor.IsDryRun = isDryRun;
      _progressMonitor.Start();

      var task = _parallelFileSystem.CompactDirectoryAsync(
        sourceDirectory,
        destinationPath,
        fileComparer,
        isDryRun);
      _parallelFileSystem.WaitForTask(task);
      _progressMonitor.Stop();
      return _progressMonitor.GetStatistics();
    }

    private static void DisplayBanner() {
      Console.WriteLine();
      Console.WriteLine("-------------------------------------------------------------------------------");
      Console.WriteLine("MTCOMPACT :: Multi-Threaded File Compacting (Cloning) - version {0}",
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0");
      Console.WriteLine("-------------------------------------------------------------------------------");
      Console.WriteLine();
    }

    private static void DisplayResults(Statistics statistics, bool isDryRun = false) {
      ProgramHelpers.DisplayCompactStatistics(statistics, isDryRun);
    }
  }
}
