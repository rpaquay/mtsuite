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

namespace mtfind {

  public abstract class SearchPatternPartMatcher {
    public abstract bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition);

    public static bool MatchAll(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      if (matcherPosition >= matchers.Count) {
        return true;
      }
      return matchers[matcherPosition].Match(value, valuePosition, matchers, matcherPosition);
    }
  }

  public class MatchAnySequence : SearchPatternPartMatcher {
    public override bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      // Note: We need to go up to "value.Length" because we must match "empty" sequence,
      //       and "MatchEnd" only success when cursor reached end of string value.
      for (int position = valuePosition; position <= value.Length; position++) {
        if (MatchAll(value, position, matchers, matcherPosition + 1)) {
          return true;
        }
      }
      return false;
    }
  }

  public class MatchAnySingle : SearchPatternPartMatcher {
    public override bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      return
        // At least one character left in the string
        (valuePosition < value.Length) &&
        // Match the rest of the string
        MatchAll(value, valuePosition + 1, matchers, matcherPosition + 1);
    }
  }

  public class MatchString : SearchPatternPartMatcher {
    private readonly string _stringValue;

    public MatchString(string stringValue) {
      _stringValue = stringValue;
    }

    public string StringValue => _stringValue;

    public override bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      return
        // Match _stringValue at the current position
        string.Compare(value, valuePosition, _stringValue, 0, _stringValue.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
        // Match the rest of the string
        MatchAll(value, valuePosition + _stringValue.Length, matchers, matcherPosition + 1);
    }
  }

  public class MatchStringAfter : SearchPatternPartMatcher {
    private readonly string _stringValue;

    public MatchStringAfter(string stringValue) {
      _stringValue = stringValue;
    }

    public string StringValue => _stringValue;

    public override bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      while (true) {
        // Look for string starting at "valuePostion"
        int indexOf = value.IndexOf(StringValue, valuePosition, StringComparison.OrdinalIgnoreCase);
        if (indexOf < 0) {
          return false;
        }
        // Advance to position after found string
        valuePosition = indexOf + _stringValue.Length;

        // Match the rest of the string after what we found
        if (MatchAll(value, valuePosition, matchers, matcherPosition + 1)) {
          return true;
        }
      }
    }
  }

  public class ExactMatch : SearchPatternPartMatcher {
    private readonly string _stringValue;

    public ExactMatch(string stringValue) {
      _stringValue = stringValue;
    }

    public string StringValue => _stringValue;

    public override bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      return
        string.Equals(value, _stringValue, StringComparison.OrdinalIgnoreCase) &&
        MatchAll(value, valuePosition, matchers, matcherPosition + 1);
    }
  }

  public class MatchSuccess : SearchPatternPartMatcher {
    public override bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      return MatchAll(value, valuePosition, matchers, matcherPosition + 1);
    }
  }

  public class ProbeString : SearchPatternPartMatcher {
    private readonly string _stringValue;

    public ProbeString(string stringValue) {
      _stringValue = stringValue;
    }

    public string StringValue => _stringValue;

    public override bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      return value.IndexOf(_stringValue, StringComparison.OrdinalIgnoreCase) >= 0 &&
        MatchAll(value, valuePosition, matchers, matcherPosition + 1);
    }
  }

  public class MatchStart : SearchPatternPartMatcher {
    public override bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      return valuePosition == 0 &&
        MatchAll(value, valuePosition, matchers, matcherPosition + 1);
    }
  }

  public class MatchEnd : SearchPatternPartMatcher {
    public override bool Match(string value, int valuePosition, List<SearchPatternPartMatcher> matchers, int matcherPosition) {
      return valuePosition == value.Length &&
        MatchAll(value, valuePosition, matchers, matcherPosition + 1);
    }
  }
}