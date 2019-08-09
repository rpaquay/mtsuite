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
using System.Text;

namespace mtfind {
  public class SearchPatternParser {
    public SearchPatternMatcher ParsePattern(string pattern) {
      List<SearchPatternPartMatcher> partMatchers = new List<SearchPatternPartMatcher>();
      partMatchers.Add(new MatchStart());
      var sb = new StringBuilder();
      foreach (var ch in pattern) {
        switch (ch) {
          case '*':
            if (sb.Length > 0) {
              partMatchers.Add(new MatchString(sb.ToString()));
              sb.Clear();
            }
            partMatchers.Add(new MatchAnySequence());
            break;

          case '?':
            if (sb.Length > 0) {
              partMatchers.Add(new MatchString(sb.ToString()));
              sb.Clear();
            }
            partMatchers.Add(new MatchAnySingle());
            break;

          default:
            sb.Append(ch);
            break;
        }
      }
      if (sb.Length > 0) {
        partMatchers.Add(new MatchString(sb.ToString()));
        sb.Clear();
      }
      partMatchers.Add(new MatchEnd());
      partMatchers = OptimizeMatcherList(partMatchers);
      return new SearchPatternMatcher(partMatchers);
    }

    private static List<SearchPatternPartMatcher> OptimizeMatcherList(List<SearchPatternPartMatcher> partMatchers) {
#if DISABLE_OPT
      return partMatchers;
#else
      List<SearchPatternPartMatcher> result = new List<SearchPatternPartMatcher>(partMatchers);
      // <any-seq><any-seq> can be replaced with <any-seq>
      Replace(result,
        new[] { typeof(MatchAnySequence), typeof(MatchAnySequence) },
        index => new NewPartMatcherReplacement(true, index, new MatchAnySequence()));

      // <start><string><end> can be replaced with <exact-match>
      Replace(result,
        new[] { typeof(MatchStart), typeof(MatchString), typeof(MatchEnd) },
        index => new NewPartMatcherReplacement(true, 0, new ExactMatch(((MatchString)result[index + 1]).StringValue)));

      // <any-seq><string> can be replaced with <string-after>
      Replace(result,
        new[] { typeof(MatchAnySequence), typeof(MatchString) },
        index => new NewPartMatcherReplacement(true, index, new MatchStringAfter(((MatchString)result[index + 1]).StringValue)));

      // (...)<string>(...) can be replaced with <string-probe>(...)<string>(...)
      // This is because string.IndexOf constant factor is much lower
      Replace(result,
        new[] { typeof(MatchString) },
        index => new NewPartMatcherReplacement(false, 0, new ProbeString(((MatchString)result[index]).StringValue)));

      // <any-seq><end> can be replaced with [] (empty sequence)
      Replace(result,
        new[] { typeof(MatchAnySequence), typeof(MatchEnd) },
        index => new NewPartMatcherReplacement(true, index, null));
      return result;
#endif
    }

    public class NewPartMatcherReplacement {
      private readonly bool _removePrevious;
      private readonly int _insertIndex;
      private readonly SearchPatternPartMatcher _partMatcher;

      public NewPartMatcherReplacement(bool removePrevious, int insertIndex, SearchPatternPartMatcher partMatcher) {
        _removePrevious = removePrevious;
        _insertIndex = insertIndex;
        _partMatcher = partMatcher;
      }

      public bool RemovePrevious => _removePrevious;
      public int InsertIndex => _insertIndex;
      public SearchPatternPartMatcher PartMatcher => _partMatcher;
    }

    private static void Replace(List<SearchPatternPartMatcher> partMatchers, IList<Type> types, Func<int, NewPartMatcherReplacement> newPartMatcher) {
      for (var i = 0; i <= (partMatchers.Count - types.Count); i++) {
        bool found = true;
        for (var typeIndex = 0; typeIndex < types.Count; typeIndex++) {
          if (!partMatchers[i + typeIndex].GetType().Equals(types[typeIndex])) {
            found = false;
            break;
          }
        }
        if (found) {
          var replacement = newPartMatcher(i);
          if (replacement.RemovePrevious) {
            partMatchers.RemoveRange(i, types.Count);
          } else {
            i += types.Count;
          }
          if (replacement.PartMatcher != null) {
            partMatchers.Insert(replacement.InsertIndex, replacement.PartMatcher);
          }
        }
      }
    }
  }
}