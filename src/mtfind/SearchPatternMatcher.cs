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

using System.Collections.Generic;

namespace mtfind {
  public class SearchPatternMatcher {
    private List<SearchPatternPartMatcher> _matchers;

    public SearchPatternMatcher(List<SearchPatternPartMatcher> matchers) {
      _matchers = matchers;
    }

    public bool MatchString(string value) {
      return SearchPatternPartMatcher.MatchAll(value, 0, _matchers, 0);
    }
  }
}