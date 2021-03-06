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

namespace mtsuite.shared {
  public class PrinterEntry {
    public PrinterEntry(string displayName, string value = null, Align valueAlign = Align.Left, string valueUnit = null,
      string shortName = null, int indent = 0, string extraValue = null) {
      DisplayName = displayName;
      Value = value;
      ValueAlign = valueAlign;
      ShortName = shortName;
      Indent = indent;
      ExtraValue = extraValue;
      ValueUnit = valueUnit;
    }

    public int Indent { get; }
    public string DisplayName { get; }
    public string Value { get; }
    public Align ValueAlign { get; }
    public string ShortName { get; }
    public string ExtraValue { get; }
    public string ValueUnit { get; }
  }

  public enum Align {
    Left,
    Right
  }
}