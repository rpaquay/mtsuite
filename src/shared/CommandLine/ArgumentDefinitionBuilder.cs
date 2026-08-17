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

namespace mtsuite.shared.CommandLine {
  public class ArgumentDefinitionBuilder {
    private readonly List<ArgDef> _definitions = new List<ArgDef>();

    public IList<ArgDef> Build() {
      return _definitions;
    }

    public ArgumentDefinitionBuilder WithPositional(string id, string description, bool isMandatory, string defaultValue = null) {
      var def = new PositionalArgDef {
        Id = id,
        Description = description,
        IsMandatory = isMandatory,
        DefaultValue = defaultValue == null ? null : new LazyDefault<string>(defaultValue),
      };
      _definitions.Add(def);
      return this;
    }

    public ArgumentDefinitionBuilder WithPositional(string id, string description, bool isMandatory, Func<string> defaultValueFactory) {
      var def = new PositionalArgDef {
        Id = id,
        Description = description,
        IsMandatory = isMandatory,
        DefaultValue = defaultValueFactory == null ? null : new LazyDefault<string>(defaultValueFactory),
      };
      _definitions.Add(def);
      return this;
    }

    public ArgumentDefinitionBuilder WithMultiplePositional(string id, string description, bool isMandatory, string defaultValue = null) {
      var def = new MultiplePositionalArgDef {
        Id = id,
        Description = description,
        IsMandatory = isMandatory,
        DefaultValue = defaultValue == null ? null : new LazyDefault<IList<string>>(() => new List<string> { defaultValue }),
      };
      _definitions.Add(def);
      return this;
    }

    public ArgumentDefinitionBuilder WithMultiplePositional(string id, string description, bool isMandatory, Func<IList<string>> defaultValueFactory) {
      var def = new MultiplePositionalArgDef {
        Id = id,
        Description = description,
        IsMandatory = isMandatory,
        DefaultValue = defaultValueFactory == null ? null : new LazyDefault<IList<string>>(defaultValueFactory),
      };
      _definitions.Add(def);
      return this;
    }

    public ArgumentDefinitionBuilder WithFlag(string id, string description, string shortName, string altShortName = "", string longName = "", bool defaultValue = false, bool allowNegation = true) {
      var def = new FlagArgDef {
        Id = id,
        Description = description,
        ShortName = shortName,
        AltShortName = altShortName,
        LongName = longName,
        DefaultValue = defaultValue,
        AllowNegation = allowNegation,
      };
      _definitions.Add(def);
      return this;
    }

    public ArgumentDefinitionBuilder WithIntOption(
      string id,
      string description,
      string shortName,
      string valueName,
      int defaultValue,
      Func<int, string> validator = null,
      string altShortName = "",
      string longName = "",
      Func<string, int?> stringParser = null) {
      var def = new IntOptionArgDef {
        Id = id,
        Description = description,
        ShortName = shortName,
        ValueName = valueName,
        DefaultValue = defaultValue,
        Validator = validator,
        AltShortName = altShortName,
        LongName = longName,
        StringParser = stringParser,
      };
      _definitions.Add(def);
      return this;
    }

    public ArgumentDefinitionBuilder WithStringOption(string id, string description, string shortName, string valueName, string defaultValue, Func<string, string> validator = null, string altShortName = "", string longName = "", bool isMandatory = false) {
      var def = new StringOptionArgDef {
        Id = id,
        Description = description,
        ShortName = shortName,
        ValueName = valueName,
        DefaultValue = defaultValue,
        IsMandatory = isMandatory,
        Validator = validator,
        AltShortName = altShortName,
        LongName = longName,
      };
      _definitions.Add(def);
      return this;
    }

    public ArgumentDefinitionBuilder WithEnumOption(
      string id,
      string description,
      string shortName,
      string valueName,
      string defaultValue,
      IEnumerable<EnumOptionValue> values,
      string altShortName = "",
      string longName = "",
      bool isMandatory = false) {
      var def = new EnumOptionArgDef {
        Id = id,
        Description = description,
        ShortName = shortName,
        ValueName = valueName,
        DefaultValue = defaultValue,
        Values = values.ToList(),
        AltShortName = altShortName,
        LongName = longName,
        IsMandatory = isMandatory,
      };
      _definitions.Add(def);
      return this;
    }

    public ArgumentDefinitionBuilder WithHelpFlag() {
      return WithFlag("help", "Display help", "h", "?", "help", defaultValue: false, allowNegation: false);
    }

    public ArgumentDefinitionBuilder WithGcFlag() {
      return WithFlag("gc", "Display .NET Garbage Collector statistics", "gc");
    }

    public ArgumentDefinitionBuilder WithProgressOption() {
      var values = new[] {
        new EnumOptionValue { Name = "none", Description = "No progress report" },
        new EnumOptionValue { Name = "line", Description = "Single line progress report" },
        new EnumOptionValue { Name = "default", Description = "Multiline progress report (but not per thread output)" },
        new EnumOptionValue { Name = "full", Description = "Multiline + per thread output" }
      };

      return WithEnumOption(
        "progress",
        "Configure progress reporting (alias: -np/--no-progress maps to 'none')",
        "p",
        "mode",
        "default",
        values,
        longName: "progress"
      );
    }

    public ArgumentDefinitionBuilder WithThreadCountOption() {
      return WithIntOption(
        "thread-count",
        "Determine the # of concurrent threads (minimum=1, \"all\"=# of CPU cores, default=min(# cores, 16))",
        "t",
        "count",
        Math.Min(Environment.ProcessorCount, 16),
        value => {
          if (value < 1)
            return "Thread count must be greater or equal to 1";
          return null;
        },
        "",
        "threads",
        stringParser: value => {
          if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
            return Environment.ProcessorCount;
          return null;
        });
    }
  }
}