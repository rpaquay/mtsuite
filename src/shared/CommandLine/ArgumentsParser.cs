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
      var usedIndices = new HashSet<int>();
      
      // Phase 1: Parse named options and flags
      for (var index = 0; index < _args.Count; ) {
        var argString = _args[index];
        if (StartsWithNamedArgumentPrefix(argString)) {
          var prefixCount = argString.StartsWith("--") ? 2 : 1;
          var argName = argString.Substring(prefixCount);

          if (argName == "no-progress" || argName == "np") {
            var progressDef = _argumentDefinitions.OfType<EnumOptionArgDef>().SingleOrDefault(x => x.Id == "progress");
            if (progressDef != null) {
              _parserArguments.Add(new ParsedArgument(progressDef, "none"));
              usedIndices.Add(index);
              index++;
              continue;
            }
          }

          var argDef = FindNamedArgument(argName, out var isNegativeFlag);
          if (argDef == null) {
            _errors.Add(string.Format("Unknown argument \"{0}\"", argString));
            usedIndices.Add(index);
            index++;
            continue;
          }

          int argsConsumed = 1;
          string argStringValue = "";

          if (argDef is OptionArgDef) {
            if (index + 1 < _args.Count && !StartsWithNamedArgumentPrefix(_args[index + 1])) {
              argStringValue = _args[index + 1];
              argsConsumed = 2;
            }
          }

          object argValue;
          if (argDef is FlagArgDef) {
            argValue = !isNegativeFlag;
          } else {
            argValue = FindArgumentValue(argString, argDef, argStringValue);
          }

          if (argValue != null) {
            _parserArguments.Add(new ParsedArgument(argDef, argValue));
          }

          for (int i = 0; i < argsConsumed; i++) {
            usedIndices.Add(index + i);
          }
          index += argsConsumed;
        } else {
          index++;
        }
      }

      // Phase 2: Parse positional arguments
      if (_errors.Count == 0) {
        // Find unused indices
        var providedFreeArgs = new List<int>();
        for (int i = 0; i < _args.Count; i++) {
          if (!usedIndices.Contains(i)) {
            providedFreeArgs.Add(i);
          }
        }

        // Get positional definitions
        var definedFreeArgs = _argumentDefinitions.Where(IsPositionalArgDef).ToList();

        // Align them right-to-left
        int valueIndex = 0;
        var multipleFreeArgStrings = new List<string>();

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
              var parsedArg = new ParsedArgument(def, multipleFreeArgStrings);
              _parserArguments.Add(parsedArg);
              while (valueIndex < providedFreeArgs.Count) {
                multipleFreeArgStrings.Add(_args[providedFreeArgs[valueIndex]]);
                valueIndex++;
              }
            } else {
              _parserArguments.Add(new ParsedArgument(def, _args[providedFreeArgs[valueIndex]]));
              valueIndex++;
            }
          }
        }

        // Any remaining unused arguments are extra
        while (valueIndex < providedFreeArgs.Count) {
          _errors.Add(string.Format("Extra argument \"{0}\"", _args[providedFreeArgs[valueIndex]]));
          valueIndex++;
        }
      }

      AddMissingDefaultValues();
      if (_errors.Count == 0) {
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

      var flagDefaults = _argumentDefinitions.OfType<FlagArgDef>()
        .Where(x => !Contains(x.Id));
      foreach (var x in flagDefaults) {
        _parserArguments.Add(new ParsedArgument(x, x.DefaultValue));
      }
    }

    private static bool IsPositionalArgDef(ArgDef argDef) {
      return argDef is PositionalArgDef or MultiplePositionalArgDef;
    }

    private object FindArgumentValue(string argString, NameArgDef argDef, string argValue) {
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

        var enumDef = valueArgDef as EnumOptionArgDef;
        if (enumDef != null) {
          if (string.IsNullOrEmpty(argValue)) {
            argValue = (string)enumDef.DefaultValue;
          }
          var matchedValue = enumDef.Values.FirstOrDefault(v => string.Equals(v.Name, argValue, StringComparison.OrdinalIgnoreCase));
          if (matchedValue == null) {
            var allowedValues = string.Join(", ", enumDef.Values.Select(v => $"\"{v.Name}\""));
            _errors.Add(string.Format("Argument \"{0}\" value \"{1}\" is invalid. Allowed values are: {2}", argString, argValue, allowedValues));
            return null;
          }
          return matchedValue.Name;
        }

        return argValue;
      }

      _errors.Add(String.Format("Argument \"{0}\" not recognized", argString));
      return null;
    }

    private NameArgDef FindNamedArgument(string name, out bool isNegativeFlag) {
      isNegativeFlag = false;
      var exactMatch = _argumentDefinitions
        .OfType<NameArgDef>()
        .SingleOrDefault(x => x.ShortName == name || x.AltShortName == name || x.LongName == name);
      if (exactMatch != null) {
        return exactMatch;
      }
      if (name.StartsWith("no-")) {
        var flagName = name.Substring(3);
        var flagMatch = _argumentDefinitions
          .OfType<FlagArgDef>()
          .SingleOrDefault(x => x.LongName == flagName && x.AllowNegation);
        if (flagMatch != null) {
          isNegativeFlag = true;
          return flagMatch;
        }
      }
      return null;
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