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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;

using mtsuite.CoreFileSystem;
using mtsuite.shared;
using mtsuite.shared.CommandLine;
using mtsuite.shared.FileNameMatching;
using mtsuite.shared.Utils;

namespace mtgrep {
  public class MtGrep {
    private readonly IFileSystem _fileSystem;
    private readonly ParallelFileSystem _parallelFileSystem;
    private readonly MtGrepProgressMonitor _progressMonitor;

    public MtGrep(IFileSystem fileSystem) {
      _fileSystem = fileSystem;
      _parallelFileSystem = new ParallelFileSystem(fileSystem);
      _progressMonitor = new MtGrepProgressMonitor();

      _parallelFileSystem.Error += exception => _progressMonitor.OnError(exception);
      _parallelFileSystem.Pulse += () => _progressMonitor.Pulse();

      _parallelFileSystem.EntriesDiscovered += (entry, list) => _progressMonitor.OnEntriesDiscovered(entry, list);
      _parallelFileSystem.DirectoryTraversing += (entry) => _progressMonitor.OnDirectoryTraversing(entry);
      _parallelFileSystem.DirectoryTraversed += (entry) => _progressMonitor.OnDirectoryTraversed(entry);
    }

    public void Run(string[] args) {
      var arguments = new MtGrepArguments(args);
      if (!arguments.IsValid || arguments.Values.Help) {
        DisplayBanner();
        if (!arguments.Values.Help) {
          arguments.DisplayArgumentErrors();
        }
        arguments.DisplayUsage();
        throw new CommandLineReturnValueException(16); // To match robocopy
      }

      var sourcePath = ProgramHelpers.MakeFullPath(arguments.Values.Directory);
      var filePattern = arguments.Values.FilePattern;
      var searchPattern = arguments.Values.SearchPattern;
      ProgramHelpers.SetWorkerThreadCount(arguments.Values.ThreadCount);
      bool followLinks = !arguments.Values.NoFollowLinks;
      bool isPlainOutput = arguments.Values.PlainOutput;
      if (!isPlainOutput) {
        DisplayBanner();
      }

      var grepResult = DoGrep(sourcePath, filePattern, searchPattern, isPlainOutput, arguments.Values.NoProgress, followLinks);

      DisplayMatchesFiles(grepResult, filePattern, searchPattern, isPlainOutput);

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
      Console.WriteLine("MTFIND :: Multi-Threaded File Grep for Windows - version {0}",
        Assembly.GetExecutingAssembly().GetName().Version);
      Console.WriteLine("-------------------------------------------------------------------------------");
      Console.WriteLine();
    }

    public List<GrepFileResult> DoGrep(FullPath sourcePath, string fileNamePattern, string searchPattern, bool isPlainOutput, bool noProgressOutput, bool followLinks) {
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
        Console.WriteLine("Search files matching pattern \"{0}\" for string \"{1}\" in \"{2}\"", fileNamePattern, searchPattern, PathHelpers.StripLongPathPrefix(sourcePath.FullName));
        Console.WriteLine();
      }
      _progressMonitor.Start();
      var collector = new MtGrepSummaryCollector(_progressMonitor, CreateFileNameMatcher(fileNamePattern), CreateGrepMatcher(searchPattern));
      var task = _parallelFileSystem.TraverseDirectoryAsync(sourceDirectory, collector, followLinks);
      _parallelFileSystem.WaitForTask(task);
      _progressMonitor.Stop();
      return collector.GrepResults;
    }

    private static FileNameMatcher CreateFileNameMatcher(string pattern) {
      var matcher = new SearchPatternParser().ParsePattern(pattern, SearchPatternParser.Options.Optimize);
      return entry => matcher.MatchString(entry.Path.Name);
    }

    private static GrepMatcher CreateGrepMatcher(string pattern) {
      return new GrepFileSearch(pattern).SearchFile;
    }

    public class GrepFileSearch {
      private readonly string _pattern;
      private readonly IList<GrepEntry> _emptyResult = new ReadOnlyCollection<GrepEntry>(new List<GrepEntry>());

      public GrepFileSearch(string pattern) {
        _pattern = pattern;
      }

      public IList<GrepEntry> SearchFile(IFileSystem fileSystem, FileSystemEntry entry) {
        if (!entry.IsFile) {
          return _emptyResult;
        }

        // Skip small files
        if (_pattern.Length > entry.FileSize) {
          return _emptyResult;
        }

        // Create collection lazily in case there are no matches
        using (var stream = fileSystem.OpenFile(entry.Path, FileAccess.Read)) {
          return SearchStream(stream, entry);
        }
      }

      public IList<GrepEntry> SearchStream(FileStream stream, FileSystemEntry entry) {
        var grepStream = new GrepStream(stream);
        if (grepStream.IsBinary()) {
          return _emptyResult;
        }

        // Reset stream
        stream.Position = 0;

        // Create collection lazily in case there are no matches
        IList<GrepEntry> result = null;
        using (var reader = new StreamReader(stream)) {
          int lineNumber = 0;
          long currentOffset = stream.Position;
          for (string line = reader.ReadLine(); line != null; line = reader.ReadLine()) {
            if (line.IndexOf(_pattern) >= 0) {
              // Create collection lazily in case there are no matches
              if (result == null) {
                result = new List<GrepEntry>(); ;
              }
              result.Add(new GrepEntry() {
                TextExtract = line,
                LineNumber = lineNumber,
                StartOffset = currentOffset,
                EndOffset = currentOffset + line.Length
              });
            }
            lineNumber++;
          }
        }
        return result ?? _emptyResult;
      }

      public class GrepStream {
        private readonly FileStream _stream;
        private readonly byte[] _buffer = new byte[1_024];
        private int _bufferLength;
        private int _bufferOffset;
        private bool _eof;

        public bool EOF => _eof;
        public GrepStream(FileStream stream) {
          _stream = stream;
        }

        public bool IsBinary() {
          EnsureBuffer();
          int asciiCount = 0;
          for(var i = 0; i < _bufferLength; i++) {
            if (IsAscii(_buffer[i])) {
              asciiCount++;
            }
          }

          float asciiRatio = (float)asciiCount / (float)_bufferLength;
          RestartBuffer();
          return asciiRatio <= 0.8;
        }

        private void RestartBuffer() {
          _bufferOffset = 0;
        }

        private static bool IsAscii(byte v) {
          return (v >= 32 && v <= 126);
        }

        private void EnsureBuffer() {
          if (_bufferOffset >= _bufferLength) {
            var count = _stream.Read(_buffer, 0, _buffer.Length);
            _eof = (count == 0);
            _bufferLength = count;
            _bufferOffset = 0;
          }
        }
      }
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

    private static void DisplayMatchesFiles(List<GrepFileResult> matchedFiles, string filePattern, string searchPattern, bool isPlainOutput) {
      var matchedEntries = matchedFiles
        .OrderBy(entry => entry.Path)
        .ToList();

      foreach (var entry in matchedEntries) {
        Console.WriteLine(PathHelpers.StripLongPathPrefix(entry.Path.FullName));
      }
      if (!isPlainOutput) {
        Console.WriteLine();
        Console.WriteLine("Found {0} files matchin pattern \"{1}\" and containing string \"{2}\"", matchedEntries.Count, filePattern, searchPattern);
      }
    }
  }
}
