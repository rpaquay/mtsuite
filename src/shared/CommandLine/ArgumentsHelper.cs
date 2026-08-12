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

namespace mtsuite.shared.CommandLine {
  public class ArgumentsHelper {
    public static string BuildUsageSummary(IList<ArgDef> argumentDefinitions) {
      var sb = new ArgumentUsageSummaryBuilder();
      foreach (var argDef in argumentDefinitions) {
        DispatchArgumentDefintion(argDef, sb);
      }
      return sb.Text;
    }

    public static void PrintArgumentUsageSummary(IList<ArgDef> argumentDefinitions) {
      foreach (var argDef in argumentDefinitions) {
        var sb = new ArgumentUsageBuilder();
        DispatchArgumentDefintion(argDef, sb);
        Console.WriteLine(sb.Text);
      }
    }

    public static void DispatchArgumentDefintion(ArgDef argDef, IArgumentDefinitionVisitor visitor) {
      var positionalDef = argDef as PositionalArgDef;
      if (positionalDef != null) {
        visitor.Visit(positionalDef);
        return;
      }

      var multiplePositionalDef = argDef as MultiplePositionalArgDef;
      if (multiplePositionalDef != null) {
        visitor.Visit(multiplePositionalDef);
        return;
      }

      var flagDef = argDef as FlagArgDef;
      if (flagDef != null) {
        visitor.Visit(flagDef);
        return;
      }

      var intDef = argDef as IntOptionArgDef;
      if (intDef != null) {
        visitor.Visit(intDef);
        return;
      }

      var stringDef = argDef as StringOptionArgDef;
      if (stringDef != null) {
        visitor.Visit(stringDef);
        return;
      }

      throw new ArgumentException("Unknown argument definition type", "argDef");
    }
  }
}