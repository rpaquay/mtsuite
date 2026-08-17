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
using System.Text;

namespace mtsuite.shared.CommandLine {
  public class ArgumentUsageBuilder : IArgumentDefinitionVisitor {
    private const string Delimiter = "--";
    private const string ShortDelimiter = "-";
    private const int ColumnWidth = 37;
    private const int IndentWidth = ColumnWidth + 3; // 2 spaces prefix + 1 space separator
    private readonly StringBuilder _sb = new StringBuilder();

    public string Text {
      get { return _sb.ToString(); }
    }

    public override string ToString() {
      return Text;
    }

    private void Append(string format, params object[] args) {
      if (_sb.Length > 0)
        _sb.Append(' ');
      _sb.Append(string.Format(format, args));
    }

    public void Visit(PositionalArgDef arg) {
      Append("  {0,-" + ColumnWidth + "} {1}", arg.Id, FormatMultiLine(arg.Description, IndentWidth));
    }

    public void Visit(MultiplePositionalArgDef arg) {
      Append("  {0,-" + ColumnWidth + "} {1}", arg.Id, FormatMultiLine(arg.Description, IndentWidth));
    }

    public void Visit(FlagArgDef arg) {
      if (string.IsNullOrEmpty(arg.LongName)) {
        var valueSummary = string.Format("{0}{1}", Delimiter, arg.ShortName);
        var description = string.Format("{0} (default: {1})", arg.Description, arg.DefaultValue.ToString().ToLower());
        Append("  {0,-" + ColumnWidth + "} {1}", valueSummary, FormatMultiLine(description, IndentWidth));
      } else {
        var valueSummary = arg.AllowNegation 
          ? string.Format("{0}{1}, {0}no-{1}", Delimiter, arg.LongName)
          : string.Format("{0}{1}", Delimiter, arg.LongName);
        var description = string.Format("{0} (short: {1}{2}, default: {3})", 
          arg.Description, ShortDelimiter, arg.ShortName, arg.DefaultValue.ToString().ToLower());
        Append("  {0,-" + ColumnWidth + "} {1}", valueSummary, FormatMultiLine(description, IndentWidth));
      }
    }

    public void Visit(IntOptionArgDef arg) {
      if (string.IsNullOrEmpty(arg.LongName)) {
        var valueSummary = string.Format("{0}{1} {2}", Delimiter, arg.ShortName, arg.ValueName);
        Append("  {0,-" + ColumnWidth + "} {1}", valueSummary, FormatMultiLine(arg.Description, IndentWidth));
      } else {
        var valueSummary = string.Format("{0}{1} {2}", Delimiter, arg.LongName, arg.ValueName);
        Append("  {0,-" + ColumnWidth + "} {1}", valueSummary, FormatMultiLine(arg.Description, IndentWidth, arg.ShortName));
      }
    }

    public void Visit(StringOptionArgDef arg) {
      if (string.IsNullOrEmpty(arg.LongName)) {
        var valueSummary = string.Format("{0}{1} {2}", Delimiter, arg.ShortName, arg.ValueName);
        Append("  {0,-" + ColumnWidth + "} {1}", valueSummary, FormatMultiLine(arg.Description, IndentWidth));
      } else {
        var valueSummary = string.Format("{0}{1} {2}", Delimiter, arg.LongName, arg.ValueName);
        Append("  {0,-" + ColumnWidth + "} {1}", valueSummary, FormatMultiLine(arg.Description, IndentWidth, arg.ShortName));
      }
    }

    public void Visit(EnumOptionArgDef arg) {
      string valueSummary;
      string descriptionText;
      if (string.IsNullOrEmpty(arg.LongName)) {
        valueSummary = string.Format("{0}{1} {2}", Delimiter, arg.ShortName, arg.ValueName);
        descriptionText = arg.Description;
      } else {
        valueSummary = string.Format("{0}{1} {2}", Delimiter, arg.LongName, arg.ValueName);
        descriptionText = string.Format("{0} (short: {1}{2})", arg.Description, ShortDelimiter, arg.ShortName);
      }

      var sb = new StringBuilder();
      sb.Append(FormatMultiLine(descriptionText, IndentWidth));

      foreach (var val in arg.Values) {
        sb.AppendLine();
        sb.Append(new string(' ', IndentWidth));
        sb.AppendFormat("  {0,-10} : {1}", val.Name, val.Description);
      }

      Append("  {0,-" + ColumnWidth + "} {1}", valueSummary, sb.ToString());
    }

    private static string FormatMultiLine(string description, int indent, string shortArgName) {
      return FormatMultiLine(string.Format("{0} (short: {1}{2})", description, ShortDelimiter, shortArgName), indent);
    }
    private static string FormatMultiLine(string description, int indent) {
      var sb = new StringBuilder();
      int index = 0;
      foreach (var line in SplitLines(description)) {
        if (index > 0) {
          sb.AppendLine();
          sb.Append(new string(' ', indent));
        }
        sb.Append(line);
        index++;
      }
      return sb.ToString();
    }

    private static IEnumerable<string> SplitLines(string value) {
      while (true) {
        int index = value.IndexOf(Environment.NewLine, StringComparison.OrdinalIgnoreCase);
        if (index < 0) {
          yield return value;
          yield break;
        }

        var current = value.Substring(0, index);
        yield return current;
        value = value.Substring(index + Environment.NewLine.Length);
      }
    }
  }
}