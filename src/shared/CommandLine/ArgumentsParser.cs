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
using mtsuite.CoreFileSystem;

namespace mtsuite.shared.CommandLine {
  public class ArgumentsParser {
    private readonly IList<ArgDef> _argumentDefinitions;
    private readonly IList<string> _args;
    private readonly IList<ParsedArgument> _parserArguments = new List<ParsedArgument>();
    private readonly IList<string> _errors = new List<string>();

    public ArgumentsParser(IList<ArgDef> argumentDefinitions, string[] args) {
      _argumentDefinitions = argumentDefinitions;
      _args = args;
    }

    public bool IsValid {
      get { return _errors.Count == 0; }
    }

    public IList<string> Errors {
      get { return _errors; }
    }

    public void Parse() {
      var freeArgMap = MapFreeArguments();
      var multipleFreeArgStrings = new List<string>();
      ParsedArgument multipleFreeArgStringsArgument = null;
      for (var index = 0; index < _args.Count; ) {
        // If the argument a named argument?
        var argString = _args[index];
        if (StartsWithNamedArgumentPrefix(argString)) {
          var prefixCount = argString.StartsWith("--") ? 2 : 1;
          var argName = argString.Substring(prefixCount);

          var argDef = FindNamedArgument(argName);
          if (argDef == null) {
            _errors.Add(string.Format("Unknown argument \"{0}\"", argString));
            index++;
            continue;
          }

          int argsConsumed = 1;
          string argStringValue = "";

          if (argDef is OptionArgDef && index + 1 < _args.Count && !StartsWithNamedArgumentPrefix(_args[index + 1])) {
            argStringValue = _args[index + 1];
            argsConsumed = 2;
          }

          object argValue = FindArgumentValue(argString, argDef, argStringValue);
          if (argValue == null) {
            index += argsConsumed;
            continue;
          }

          var parsedArg = new ParsedArgument(argDef, argValue);
          _parserArguments.Add(parsedArg);
          index += argsConsumed;
        } else {
          // Handle free string arguments
          if (freeArgMap.TryGetValue(index, out var argDef)) {
            switch (argDef) {
              case PositionalArgDef: {
                var parsedArg = new ParsedArgument(argDef, argString);
                _parserArguments.Add(parsedArg);
                break;
              }
              case MultiplePositionalArgDef: {
                if (multipleFreeArgStringsArgument == null) {
                  var parsedArg = new ParsedArgument(argDef, multipleFreeArgStrings);
                  _parserArguments.Add(parsedArg);
                  multipleFreeArgStringsArgument = parsedArg;
                }
                multipleFreeArgStrings.Add(argString);
                break;
              }
            }
          } else {
            _errors.Add(string.Format("Extra argument \"{0}\"", argString));
          }
          index++;
        }
      }

      if (_errors.Count == 0) {
        AddMissingDefaultValues();
        CheckMissingMandatoryArguments();
      }
    }
    
    private static bool StartsWithNamedArgumentPrefix(string argString) {
      return (PathHelpers.DirectorySeparatorString == "/")
        ? argString.StartsWith("-") || argString.StartsWith("--")
        : argString.StartsWith("/") || argString.StartsWith("-") || argString.StartsWith("--");
    }

    private void CheckMissingMandatoryArguments() {
      foreach (var argDef in _argumentDefinitions.Where(x => x.IsMandatory)) {
        if (!Contains(argDef.Id)) {
          _errors.Add(string.Format("Missing argument \"{0}\"", argDef.Id));
        }
      }
    }

    private void AddMissingDefaultValues() {
      var namedDefaults = _argumentDefinitions.OfType<OptionArgDef>()
        .Where(x => !Contains(x.Id) && x.DefaultValue != null);
      foreach (var x in namedDefaults) {
        _parserArguments.Add(new ParsedArgument(x, x.DefaultValue));
      }

      var stringDefaults = _argumentDefinitions.OfType<PositionalArgDef>()
        .Where(x => !Contains(x.Id) && x.DefaultValue != null);
      foreach (var x in stringDefaults) {
        _parserArguments.Add(new ParsedArgument(x, x.DefaultValue.Value));
      }
      
      var multiStringDefaults = _argumentDefinitions.OfType<MultiplePositionalArgDef>()
        .Where(x => !Contains(x.Id) && x.DefaultValue != null);
      foreach (var x in multiStringDefaults) {
        _parserArguments.Add(new ParsedArgument(x, x.DefaultValue.Value));
      }
    }

    private Dictionary<int, ArgDef> MapFreeArguments() {
      var map = new Dictionary<int, ArgDef>();
      
      // 1. Extract all free args from _args
      var providedFreeArgs = new List<int>(); // Indices in _args
      bool isWindows = PathHelpers.DirectorySeparatorString == "\\";
      for (int i = 0; i < _args.Count; i++) {
        if (!StartsWithNamedArgumentPrefix(_args[i])) {
          providedFreeArgs.Add(i);
        }
      }

      // 2. Get defined free args
      var definedFreeArgs = _argumentDefinitions.Where(IsPositionalArgDef).ToList();

      // 3. Align them
      int valueIndex = 0;
      for (int i = 0; i < definedFreeArgs.Count; i++) {
        if (valueIndex >= providedFreeArgs.Count) {
          break;
        }

        var def = definedFreeArgs[i];
        int remainingValues = providedFreeArgs.Count - valueIndex;
        int remainingMandatory = 0;
        for (int j = i; j < definedFreeArgs.Count; j++) {
          if (definedFreeArgs[j].IsMandatory) {
            remainingMandatory++;
          }
        }

        if (def.IsMandatory || remainingValues > remainingMandatory) {
          if (def is MultiplePositionalArgDef) {
            // Consumes all remaining free args
            while (valueIndex < providedFreeArgs.Count) {
              map[providedFreeArgs[valueIndex]] = def;
              valueIndex++;
            }
          } else {
            map[providedFreeArgs[valueIndex]] = def;
            valueIndex++;
          }
        }
      }

      // Any remaining provided free args that couldn't be matched (e.g. if too many)
      while (valueIndex < providedFreeArgs.Count) {
        // Leave them unmapped, which will trigger the "Extra argument" error in Parse
        valueIndex++;
      }

      return map;
    }

    private static bool IsPositionalArgDef(ArgDef argDef) {
      return argDef is PositionalArgDef or MultiplePositionalArgDef;
    }

    private object FindArgumentValue(string argString, NameArgDef argDef, string argValue) {
      var flagDef = argDef as FlagArgDef;
      if (flagDef != null) {
        return FlagArgDef.ValueMarker;
      }

      var valueArgDef = argDef as OptionArgDef;
      if (valueArgDef != null) {
        if (string.IsNullOrEmpty(argValue)) {
          // Note: If there is no explicit value, we'll use the default value.
          if (valueArgDef.DefaultValue == null) {
            _errors.Add(String.Format("Argument \"{0}\" requires a value", argString));
            return null;
          }
        }

        var intDef = valueArgDef as IntOptionArgDef;
        if (intDef != null) {
          // Parse argument value (or use default value)
          int value;
          if (string.IsNullOrEmpty(argValue)) {
            value = (int)intDef.DefaultValue;
          } else if (intDef.StringParser != null && intDef.StringParser(argValue) is int customValue) {
            value = customValue;
          } else if (!int.TryParse(argValue, out value)) {
            if (intDef.StringParser != null) {
              _errors.Add(String.Format("Argument \"{0}\" requires an integer value or \"all\"", argString));
            } else {
              _errors.Add(String.Format("Argument \"{0}\" requires an integer value", argString));
            }
            return null;
          }
          if (intDef.Validator != null) {
            var error = intDef.Validator(value);
            if (!string.IsNullOrEmpty(error)) {
              _errors.Add(error);
              return null;
            }
          }
          return value;
        }

        var stringDef = valueArgDef as StringOptionArgDef;
        if (stringDef != null) {
          if (string.IsNullOrEmpty(argValue)) {
            argValue = (string)stringDef.DefaultValue;
          }
          if (stringDef.Validator != null) {
            var error = stringDef.Validator(argValue);
            if (!string.IsNullOrEmpty(error)) {
              _errors.Add(error);
              return null;
            }
          }
          return argValue;
        }

        return argValue;
      }

      _errors.Add(String.Format("Argument \"{0}\" not recognized", argString));
      return null;
    }

    private NameArgDef FindNamedArgument(string name) {
      return _argumentDefinitions
        .OfType<NameArgDef>()
        .Where(x => {
          return (x.ShortName == name || x.AltShortName == name || x.LongName == name);
        }).SingleOrDefault();
    }

    public bool Contains(string id) {
      return _parserArguments.Any(x => x.ArgDef.Id == id);
    }

    public ParsedArgument this[string id] {
      get {
        return _parserArguments.Single(x => x.ArgDef.Id == id);
      }
    }
  }
}