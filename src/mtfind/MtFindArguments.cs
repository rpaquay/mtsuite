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
using mtsuite.shared.CommandLine;

namespace mtfind {
  public class MtFindArguments {
    private readonly IList<ArgDef> _argumentDefinitions;
    private readonly ArgumentsParser _parser;
    private readonly ArgumentValues _values;

    public MtFindArguments(string[] args) {
      _argumentDefinitions = new ArgumentDefinitionBuilder()
        .WithPositional("directory-path", "The path of the directory to search (default is the current directory)", false)
        .WithPositional("pattern", "The file name pattern to search for (e.g. \"*.xml\")", false)
        .WithStringOption("name", "Another way to specify the file name pattern", "name", "pattern", null)
        .WithFlag("include-dir", "Include directory names in search", "i", "", "include-dir")
        .WithFlag("plain-output", "Plain output, i.e. only display the list of file paths that match the search pattern, without progress", "po", "", "plain-output")
        .WithFlag("follow-links", "Follow symbolic links when traversing directories", "fl", "", "follow-links")
        .WithCommonOptions()
        .Build();

      _parser = new ArgumentsParser(_argumentDefinitions, args);
      _values = new ArgumentValues(_parser);

      _parser.Parse();
      ResolveDirectoryAndPattern();
    }

    /// <summary>
    /// The pattern is either the "--name" option or the last positional argument, so that both
    /// "mtfind directory --name pattern" and "mtfind [directory] pattern" work.
    /// </summary>
    private void ResolveDirectoryAndPattern() {
      if (!_parser.IsValid) {
        return;
      }

      var directory = _parser.Contains("directory-path") ? _parser["directory-path"].StringValue : null;
      var pattern = _parser.Contains("pattern") ? _parser["pattern"].StringValue : null;
      if (_parser.Contains("name")) {
        if (pattern != null) {
          _parser.Errors.Add(string.Format("Extra argument \"{0}\"", pattern));
          return;
        }
        pattern = _parser["name"].StringValue;
      }
      else if (pattern == null) {
        pattern = directory;
        directory = null;
      }

      if (pattern == null) {
        _parser.Errors.Add("Missing argument \"pattern\"");
        return;
      }
      _values.Directory = directory ?? Environment.CurrentDirectory;
      _values.Pattern = pattern;
    }

    public bool IsValid => _parser.IsValid;

    public ArgumentValues Values => _values;

    public void DisplayUsage() {
      Console.WriteLine("Search for file names inside a directory.");
      Console.WriteLine();
      Console.WriteLine("Usage: {0} {1}", Process.GetCurrentProcess().ProcessName,
        ArgumentsHelper.BuildUsageSummary(_argumentDefinitions));
      Console.WriteLine();
      ArgumentsHelper.PrintArgumentUsageSummary(_argumentDefinitions);
    }

    public void DisplayArgumentErrors() {
      foreach (var error in _parser.Errors) {
        Console.WriteLine("ERROR: {0}", error);
      }
      Console.WriteLine();
    }

    public class ArgumentValues {
      private readonly ArgumentsParser _parser;

      public ArgumentValues(ArgumentsParser parser) {
        _parser = parser;
      }

      public bool Help => _parser["help"].BoolValue;

      public string Directory { get; internal set; }

      public string Pattern { get; internal set; }

      public int ThreadCount => _parser["thread-count"].IntValue;

      public bool GarbageCollect => _parser["gc"].BoolValue;

      public bool FollowLinks => _parser["follow-links"].BoolValue;

      public bool PlainOutput => _parser["plain-output"].BoolValue;

      public bool IncludeDir => _parser["include-dir"].BoolValue;

      public string Progress => _parser["progress"].StringValue;

      public bool ShowWarnings => _parser["show-warnings"].BoolValue;

      public bool ShowErrors => _parser["show-errors"].BoolValue;
    }
  }
}
