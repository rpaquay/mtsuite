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
using mtsuite.shared.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests {
  [TestClass]
  public class ArgumentsParserTest {
    [TestMethod]
    public void ArgumentsParserShouldWorkWithNoArgs() {
      var argumentDefinitions = new ArgDef[] {
        new SwitchArgDef {
          Id = "help",
          ShortName = "h",
          AltShortName = "?",
          LongName = "help",
        },
        new IntFlagArgDef {
          Id = "depth",
          ShortName = "d",
          LongName = "depth",
          DefaultValue = 2,
        },
        new StringFlagArgDef {
          Id = "attr",
          ShortName = "a",
          LongName = "attributes",
          DefaultValue = "foo",
        },
        new FreeStringArgDef {
          Id = "directory",
          DefaultValue = Environment.CurrentDirectory,
        }
      };

      var args = new string[] {
      };
      var parser = new ArgumentsParser(argumentDefinitions, args);
      parser.Parse();
      Assert.IsTrue(parser.IsValid);
    }

    [TestMethod]
    public void ArgumentsParserShouldWorkWithSwitch() {
      var argumentDefinitions = new ArgDef[] {
        new SwitchArgDef {
          Id = "help",
          ShortName = "h",
          AltShortName = "?",
          LongName = "help",
        },
        new IntFlagArgDef {
          Id = "depth",
          ShortName = "d",
          LongName = "depth",
          DefaultValue = 2,
        },
        new StringFlagArgDef {
          Id = "attr",
          ShortName = "a",
          LongName = "attributes",
          DefaultValue = "foo",
        },
        new FreeStringArgDef {
          Id = "directory",
          DefaultValue = Environment.CurrentDirectory,
        }
      };

      var args = new[] { "-?" };
      var parser = new ArgumentsParser(argumentDefinitions, args);
      parser.Parse();
      Assert.IsTrue(parser.IsValid);
      Assert.IsTrue(parser.Contains("help"));
    }

    [TestMethod]
    public void ArgumentsParserShouldWorkWithDefaultValues() {
      var argumentDefinitions = new ArgDef[] {
        new SwitchArgDef {
          Id = "help",
          ShortName = "h",
          AltShortName = "?",
          LongName = "help",
        },
        new IntFlagArgDef {
          Id = "depth",
          ShortName = "d",
          LongName = "depth",
          DefaultValue = 2,
        },
        new StringFlagArgDef {
          Id = "attr",
          ShortName = "a",
          LongName = "attributes",
          DefaultValue = "foo",
        },
        new FreeStringArgDef {
          Id = "directory",
          DefaultValue = "defaultDir",
        }
      };

      var args = new string[] { };
      var parser = new ArgumentsParser(argumentDefinitions, args);
      parser.Parse();
      Assert.IsTrue(parser.IsValid);
      Assert.IsTrue(parser.Contains("directory"));
      Assert.IsTrue(parser.Contains("depth"));
      Assert.AreEqual(2, parser["depth"].IntValue);
      Assert.IsTrue(parser.Contains("attr"));
      Assert.AreEqual("foo", parser["attr"].StringValue);
      Assert.AreEqual("defaultDir", parser["directory"].StringValue);
    }

    [TestMethod]
    public void ArgumentsParserShouldWorkWithDefaultValueForMissingValue() {
      var argumentDefinitions = new ArgDef[] {
        new IntFlagArgDef {
          Id = "depth",
          ShortName = "d",
          LongName = "depth",
          DefaultValue = 2,
        },
      };

      var args = new string[] { "-d" };
      var parser = new ArgumentsParser(argumentDefinitions, args);
      parser.Parse();
      Assert.IsTrue(parser.IsValid);
      Assert.IsTrue(parser.Contains("depth"));
      Assert.AreEqual(2, parser["depth"].IntValue);
    }

    [TestMethod]
    public void ArgumentsParserShouldWorkWithDefaultMandatoryArguments() {
      var argumentDefinitions = new ArgDef[] {
        new FreeStringArgDef {
          Id = "directory",
          DefaultValue = "defaultDir",
          IsMandatory = true
        }
      };

      var args = new string[] { };
      var parser = new ArgumentsParser(argumentDefinitions, args);
      parser.Parse();
      Assert.IsTrue(parser.IsValid);
      Assert.IsTrue(parser.Contains("directory"));
      Assert.AreEqual("defaultDir", parser["directory"].StringValue);
    }

    [TestMethod]
    public void ArgumentsParserShouldWorkWithMissingMandatoryArguments() {
      var argumentDefinitions = new ArgDef[] {
        new FreeStringArgDef {
          Id = "directory",
          IsMandatory = true
        }
      };

      var args = new string[] { };
      var parser = new ArgumentsParser(argumentDefinitions, args);
      parser.Parse();
      Assert.IsFalse(parser.IsValid);
    }

    [TestMethod]
    public void ArgumentsParserThreadCountDefaultIsMinCoresAnd16() {
      var builder = new ArgumentDefinitionBuilder()
        .WithThreadCountSwitch();
      var parser = new ArgumentsParser(builder.Build(), Array.Empty<string>());
      parser.Parse();
      Assert.IsTrue(parser.IsValid);
      Assert.AreEqual(Math.Min(Environment.ProcessorCount, 16), parser["thread-count"].IntValue);
    }

    [TestMethod]
    public void ArgumentsParserThreadCountSupportsAll() {
      var builder = new ArgumentDefinitionBuilder()
        .WithThreadCountSwitch();

      // --threads:all
      var parser1 = new ArgumentsParser(builder.Build(), new[] { "--threads:all" });
      parser1.Parse();
      Assert.IsTrue(parser1.IsValid);
      Assert.AreEqual(Environment.ProcessorCount, parser1["thread-count"].IntValue);

      // -t:ALL
      var parser2 = new ArgumentsParser(builder.Build(), new[] { "-t:ALL" });
      parser2.Parse();
      Assert.IsTrue(parser2.IsValid);
      Assert.AreEqual(Environment.ProcessorCount, parser2["thread-count"].IntValue);
    }

    [TestMethod]
    public void ArgumentsParserThreadCountSupportsExplicitNumber() {
      var builder = new ArgumentDefinitionBuilder()
        .WithThreadCountSwitch();

      var parser1 = new ArgumentsParser(builder.Build(), new[] { "--threads:32" });
      parser1.Parse();
      Assert.IsTrue(parser1.IsValid);
      Assert.AreEqual(32, parser1["thread-count"].IntValue);

      var parser2 = new ArgumentsParser(builder.Build(), new[] { "-t:4" });
      parser2.Parse();
      Assert.IsTrue(parser2.IsValid);
      Assert.AreEqual(4, parser2["thread-count"].IntValue);
    }

    [TestMethod]
    public void ArgumentsParserThreadCountRejectsInvalid() {
      var builder = new ArgumentDefinitionBuilder()
        .WithThreadCountSwitch();

      var parser1 = new ArgumentsParser(builder.Build(), new[] { "--threads:0" });
      parser1.Parse();
      Assert.IsFalse(parser1.IsValid);

      var parser2 = new ArgumentsParser(builder.Build(), new[] { "--threads:invalid" });
      parser2.Parse();
      Assert.IsFalse(parser2.IsValid);
    }

    [TestMethod]
    public void ArgumentsParserShouldSupportOptionalPositionalFollowedByMandatoryPositional() {
      var definitions = new ArgDef[] {
        new FreeStringArgDef {
          Id = "directory",
          IsMandatory = false,
          DefaultValue = "defaultDir"
        },
        new FreeStringArgDef {
          Id = "pattern",
          IsMandatory = true
        }
      };

      // 1. User passes only 1 argument (should map to the mandatory "pattern")
      {
        var parser = new ArgumentsParser(definitions, new[] { "mypattern" });
        parser.Parse();
        Assert.IsTrue(parser.IsValid);
        Assert.AreEqual("defaultDir", parser["directory"].StringValue);
        Assert.AreEqual("mypattern", parser["pattern"].StringValue);
      }

      // 2. User passes 2 arguments (should map first to "directory" and second to "pattern")
      {
        var parser = new ArgumentsParser(definitions, new[] { "customDir", "mypattern" });
        parser.Parse();
        Assert.IsTrue(parser.IsValid);
        Assert.AreEqual("customDir", parser["directory"].StringValue);
        Assert.AreEqual("mypattern", parser["pattern"].StringValue);
      }

      // 3. User passes 0 arguments (should fail because "pattern" is mandatory)
      {
        var parser = new ArgumentsParser(definitions, Array.Empty<string>());
        parser.Parse();
        Assert.IsFalse(parser.IsValid);
      }
    }

    [TestMethod]
    public void ArgumentsParserShouldSupportMultipleOptionalPositionalsGreedyMapping() {
      var definitions = new ArgDef[] {
        new FreeStringArgDef {
          Id = "arg1",
          IsMandatory = false,
          DefaultValue = "def1"
        },
        new FreeStringArgDef {
          Id = "arg2",
          IsMandatory = false,
          DefaultValue = "def2"
        },
        new FreeStringArgDef {
          Id = "pattern",
          IsMandatory = true
        }
      };

      // Case A: 1 argument -> goes to pattern
      {
        var parser = new ArgumentsParser(definitions, new[] { "valP" });
        parser.Parse();
        Assert.IsTrue(parser.IsValid);
        Assert.AreEqual("def1", parser["arg1"].StringValue);
        Assert.AreEqual("def2", parser["arg2"].StringValue);
        Assert.AreEqual("valP", parser["pattern"].StringValue);
      }

      // Case B: 2 arguments -> goes to arg1 and pattern, arg2 takes default
      {
        var parser = new ArgumentsParser(definitions, new[] { "val1", "valP" });
        parser.Parse();
        Assert.IsTrue(parser.IsValid);
        Assert.AreEqual("val1", parser["arg1"].StringValue);
        Assert.AreEqual("def2", parser["arg2"].StringValue);
        Assert.AreEqual("valP", parser["pattern"].StringValue);
      }

      // Case C: 3 arguments -> goes to arg1, arg2, pattern
      {
        var parser = new ArgumentsParser(definitions, new[] { "val1", "val2", "valP" });
        parser.Parse();
        Assert.IsTrue(parser.IsValid);
        Assert.AreEqual("val1", parser["arg1"].StringValue);
        Assert.AreEqual("val2", parser["arg2"].StringValue);
        Assert.AreEqual("valP", parser["pattern"].StringValue);
      }
    }

    [TestMethod]
    public void ArgumentsParserPositionalDefaultValueIsLazy() {
      bool factoryEvaluated = false;
      var definitions = new ArgDef[] {
        new FreeStringArgDef {
          Id = "directory",
          IsMandatory = false,
          DefaultValue = new LazyDefault<string>(() => {
            factoryEvaluated = true;
            return "lazyDir";
          })
        },
        new FreeStringArgDef {
          Id = "pattern",
          IsMandatory = true
        }
      };

      // Case A: Positional value is provided by the user. Factory should NOT be evaluated!
      {
        var parser = new ArgumentsParser(definitions, new[] { "providedDir", "pattern" });
        parser.Parse();
        Assert.IsTrue(parser.IsValid);
        Assert.IsFalse(factoryEvaluated);
        Assert.AreEqual("providedDir", parser["directory"].StringValue);
      }

      // Case B: Positional value is NOT provided by the user. Factory should be evaluated!
      {
        factoryEvaluated = false;
        var parser = new ArgumentsParser(definitions, new[] { "pattern" });
        parser.Parse();
        Assert.IsTrue(parser.IsValid);
        Assert.IsTrue(factoryEvaluated);
        Assert.AreEqual("lazyDir", parser["directory"].StringValue);
      }
    }
  }
}
