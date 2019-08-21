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
using mtsuite.shared.CommandLine;

namespace mtfind {
  public class MtFindArguments {
    private readonly IList<ArgDef> _argumentDefinitions;
    private readonly ArgumentsParser _parser;
    private readonly ArgumentValues _values;

    public MtFindArguments(string[] args) {
      _argumentDefinitions = new ArgumentDefinitionBuilder()
        .WithHelpSwitch()
        .WithStringFlag("directory", "The directory tree to search", "d", "path", Environment.CurrentDirectory, null, "", "directory")
        .WithSwitch("plain-output", "Plain output, i.e. only display list of file paths that match the search pattern", "po", "", "plain-output")
        .WithSwitch("no-progress", "Don't display progress at regular intervals", "np", "", "no-progress")
        .WithSwitch("no-follow-links", "Don't follow symbolic links when traversing directories", "nl", "", "no-follow-links")
        .WithThreadCountSwitch()
        .WithGcSwitch()
        .WithString("pattern", "The pattern to search for", true, "*")
        .Build();

      _parser = new ArgumentsParser(_argumentDefinitions, args);
      _values = new ArgumentValues(_parser);

      _parser.Parse();
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

      public bool Help => _parser.Contains("help");

      public string Directory => _parser["directory"].StringValue;

      public string Pattern => _parser["pattern"].StringValue;

      public int ThreadCount => _parser["thread-count"].IntValue;

      public bool GarbageCollect => _parser.Contains("gc");

      public bool NoFollowLinks => _parser.Contains("no-follow-links");

      public bool PlainOutput => _parser.Contains("plain-output");

      public bool NoProgress => _parser.Contains("no-progress");
    }
  }
}
