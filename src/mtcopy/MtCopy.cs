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
using System.Diagnostics;
using System.Reflection;
using mtsuite.shared;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.shared.CommandLine;
using mtsuite.CoreFileSystem;

namespace mtcopy {
  public class MtCopy {
    private readonly IFileSystem _fileSystem;
    private readonly MtPoolFactory _poolFactory;
    private readonly IParallelFileSystem _parallelFileSystem;
    private readonly IProgressMonitor<Statistics> _progressMonitor;

    public MtCopy(IFileSystem fileSystem, MtPoolFactory poolFactory) {
      ArgumentNullException.ThrowIfNull(poolFactory);
      _fileSystem = fileSystem;
      _poolFactory = poolFactory;
      _parallelFileSystem = new ParallelFileSystem(fileSystem, poolFactory);
      _progressMonitor = new CopyProgressMonitor();

      _parallelFileSystem.Error += (path, exception) => _progressMonitor.OnError(path, exception);
      _parallelFileSystem.Pulse += () => _progressMonitor.Pulse();

      _parallelFileSystem.EntryDeleting += (entry) => _progressMonitor.OnEntryDeleting(entry);
      _parallelFileSystem.EntryDeleted += (entry, elapsed) => _progressMonitor.OnEntryDeleted(entry, elapsed);

      _parallelFileSystem.FileComparing += (entry) => _progressMonitor.OnFileComparing(entry);
      _parallelFileSystem.FileComparingProgress +=
        (entry, elapsed, bytesFromPreviousCall, bytesSoFar) => _progressMonitor.OnFileComparingProgress(entry, elapsed, bytesFromPreviousCall, bytesSoFar);
      _parallelFileSystem.FileCompared += (entry, elapsed, bytes) => _progressMonitor.OnFileCompared(entry, elapsed, bytes);
      _parallelFileSystem.FileCopying += (entry) => _progressMonitor.OnFileCopying(entry);
      _parallelFileSystem.FileCopyingProgress +=
        (entry, elapsed, bytesFromPreviousCall, bytesSoFar) => _progressMonitor.OnFileCopyingProgress(entry, elapsed, bytesFromPreviousCall, bytesSoFar);
      _parallelFileSystem.FileCopied += (entry, elapsed, bytes) => _progressMonitor.OnFileCopied(entry, elapsed, bytes);
      _parallelFileSystem.FileCloning += (entry) => _progressMonitor.OnFileCloning(entry);
      _parallelFileSystem.FileCloned += (entry, elapsed, bytes) => _progressMonitor.OnFileCloned(entry, elapsed, bytes);
      _parallelFileSystem.FileCloneSkipped += (entry) => _progressMonitor.OnFileCloneSkipped(entry, entry.FileSize);
      _parallelFileSystem.FileAlreadyCloned += (entry) => _progressMonitor.OnFileAlreadyCloned(entry, entry.FileSize);

      _parallelFileSystem.DirectoryTraversing += (entry) => _progressMonitor.OnDirectoryTraversing(entry);
      _parallelFileSystem.DirectoryTraversed += (entry, list) => _progressMonitor.OnDirectoryTraversed(entry, list);
      _parallelFileSystem.DirectoryCreated += (entry) => _progressMonitor.OnDirectoryCreated(entry);
      _parallelFileSystem.FileCopySkipped += (entry) => _progressMonitor.OnFileSkipped(entry, entry.FileSize);
    }

    public void Run(string[] args) {
      DisplayBanner();

      var argumentDefinitions = new ArgumentDefinitionBuilder()
        .WithPositional("source-path", "The path of the source directory", true)
        .WithPositional("destination-path", "The path of the destination directory", true)
        .WithFlag("fc", "Compare file contents instead of file modification time (slower)", "fc", "", "content")
        .WithFlag("ft", "Compare file modification time (default)", "ft")
        .WithFlag("noclone", "Disable file cloning (CoW) on supported platforms (e.g. macOS APFS)", "noclone")
        .WithNoProgressFlag()
        .WithThreadCountOption()
        .WithGcFlag()
        .WithHelpFlag()
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
        throw new CommandLineReturnValueException(16); // To match robocopy
      }

      var sourcePath = ProgramHelpers.MakeFullPath(parser["source-path"].StringValue);
      var destinationPath = ProgramHelpers.MakeFullPath(parser["destination-path"].StringValue);
      ProgramHelpers.SetWorkerThreadCount(parser["thread-count"].IntValue);
      _progressMonitor.ShowProgress = !parser.Contains("no-progress");
      IFileComparer fileComparer;
      if (parser.Contains("fc")) {
        fileComparer = new FileContentsFileComparer(_fileSystem, _poolFactory);
      }
      else {
        fileComparer = new LastWriteTimeFileComparer(_fileSystem);
      }

      var copyOptions = CopyOptions.DeleteMismatchedFiles | CopyOptions.SkipIdenticalFiles;
      if (parser.Contains("noclone")) {
        copyOptions |= CopyOptions.NoClone;
      }

      var statistics = DoCopy(sourcePath, destinationPath, fileComparer, copyOptions);
      DisplayResults(statistics);
      if (parser.Contains("gc")) {
        ProgramHelpers.DisplayGcStatistics(_poolFactory);
      }

      // 0 = success, 8 = fail (to match robocopy)
      if (statistics.Errors.Count > 0)
        throw new CommandLineReturnValueException(8);
    }

    private static void DisplayUsage(IList<ArgDef> argumentDefinitions) {
      Console.WriteLine("Copies all files and directories from one location to another location.");
      Console.WriteLine();
      Console.WriteLine("Usage: {0} {1}", Process.GetCurrentProcess().ProcessName,
        ArgumentsHelper.BuildUsageSummary(argumentDefinitions));
      Console.WriteLine();
      ArgumentsHelper.PrintArgumentUsageSummary(argumentDefinitions);
    }

    public Statistics DoCopy(FullPath sourcePath, FullPath destinationPath, IFileComparer fileComparer) {
      return DoCopy(sourcePath, destinationPath, fileComparer, CopyOptions.DeleteMismatchedFiles | CopyOptions.SkipIdenticalFiles);
    }

    public Statistics DoCopy(FullPath sourcePath, FullPath destinationPath, IFileComparer fileComparer, CopyOptions copyOptions) {
      // Check source exists
      FileSystemEntry sourceDirectory;
      try {
        sourceDirectory = _fileSystem.GetEntry(sourcePath);
      } catch (Exception e) {
        Console.WriteLine(e.Message);
        // 8 = fail (to match robocopy)
        throw new CommandLineReturnValueException(8);
      }

      // Lookup or create destination directory
      FileSystemEntry destinationDirectory;
      try {
        try {
          destinationDirectory = _fileSystem.GetEntry(destinationPath);
        }
        catch {
          _fileSystem.CreateDirectory(destinationPath);
          destinationDirectory = _fileSystem.GetEntry(destinationPath);
        }
      } catch (Exception e) {
        Console.WriteLine(e.Message);
        // 8 = fail (to match robocopy)
        throw new CommandLineReturnValueException(8);
      }

      Console.WriteLine("Copying files and subdirectories from \"{0}\" to \"{1}\"",
        sourcePath.FullName,
        destinationPath.FullName);
      _progressMonitor.SourcePath = sourcePath;
      _progressMonitor.DestinationPath = destinationPath;
      _progressMonitor.Start();
      //_progressMonitor.OnEntriesDiscovered(new List<FileSystemEntry>(new[] { sourceDirectory }));

      var task = _parallelFileSystem.CopyDirectoryAsync(
        sourceDirectory,
        destinationDirectory,
        copyOptions,
        fileComparer);
      _parallelFileSystem.WaitForTask(task);
      _progressMonitor.Stop();
      return _progressMonitor.GetStatistics();
    }

    private static void DisplayBanner() {
      Console.WriteLine();
      Console.WriteLine("-------------------------------------------------------------------------------");
      Console.WriteLine("MTCOPY :: Multi-Threaded Directory Copy - version {0}",
        Assembly.GetExecutingAssembly().GetName().Version.ToString());
      Console.WriteLine("-------------------------------------------------------------------------------");
      Console.WriteLine();
    }

    private static void DisplayResults(Statistics statistics) {
      ProgramHelpers.DisplayFullStatistics(statistics);
    }
  }
}
