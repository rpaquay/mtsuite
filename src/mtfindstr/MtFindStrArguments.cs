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

namespace mtfindstr {
  public class MtFindStrArguments {
    private readonly IList<ArgDef> _argumentDefinitions;
    private readonly ArgumentsParser _parser;
    private readonly ArgumentValues _values;

    public MtFindStrArguments(string[] args) {
      _argumentDefinitions = new ArgumentDefinitionBuilder()
        .WithHelpFlag()
        .WithFlag("plain-output", "Plain output, i.e. only display list of file paths that match the search pattern", "po", "", "plain-output")
        .WithProgressOption()
        .WithFlag("follow-links", "Follow symbolic links when traversing directories", "fl", "", "follow-links", defaultValue: true)
        .WithFlag("show-warnings", "Display warnings", "w", "", "warnings")
        .WithFlag("show-errors", "Display errors", "e", "", "errors", defaultValue: true)
        .WithThreadCountOption()
        .WithGcFlag()
        .WithStringOption("name", "The file name pattern to include files to search (default=\"*\")", "name", "pattern", "*")
        .WithPositional("directory", "The directory tree to search", false, () => Environment.CurrentDirectory)
        .WithPositional("pattern", "The string to find in text files (e.g. \"build\")", true)
        .Build();

      _parser = new ArgumentsParser(_argumentDefinitions, args);
      _values = new ArgumentValues(_parser);

      _parser.Parse();
    }

    public bool IsValid => _parser.IsValid;

    public ArgumentValues Values => _values;

    public void DisplayUsage() {
      Console.WriteLine("Search for strings in files.");
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

      public string Directory => _parser["directory"].StringValue;

      public IList<string> FileNamePatterns => new List<string> { _parser["name"].StringValue };

      public string SearchPattern => _parser["pattern"].StringValue;

      public int ThreadCount => _parser["thread-count"].IntValue;

      public bool GarbageCollect => _parser["gc"].BoolValue;

      public bool FollowLinks => _parser["follow-links"].BoolValue;

      public bool PlainOutput => _parser["plain-output"].BoolValue;

      public string Progress => _parser["progress"].StringValue;

      public bool ShowWarnings => _parser["show-warnings"].BoolValue;

      public bool ShowErrors => _parser["show-errors"].BoolValue;
    }
  }
}
