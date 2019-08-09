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

using Microsoft.VisualStudio.TestTools.UnitTesting;

using mtfind;

namespace tests {
  [TestClass]
  public class SearchPatternParserTest {
    [TestMethod]
    public void SimpleStringShouldWork() {
      var matcher = new SearchPatternParser().ParsePattern("foo");
      Assert.IsTrue(matcher.MatchString("foo"));
      Assert.IsFalse(matcher.MatchString(" foo"));
      Assert.IsFalse(matcher.MatchString("foo "));
      Assert.IsFalse(matcher.MatchString("foofoo"));
      Assert.IsFalse(matcher.MatchString("oof"));
      Assert.IsFalse(matcher.MatchString("fooo"));
      Assert.IsFalse(matcher.MatchString("fo"));
      Assert.IsFalse(matcher.MatchString("oof"));
    }

    [TestMethod]
    public void SimpleAsteriskShouldWork() {
      var matcher = new SearchPatternParser().ParsePattern("*foo");
      Assert.IsTrue(matcher.MatchString("foo"));
      Assert.IsTrue(matcher.MatchString(" foo"));
      Assert.IsFalse(matcher.MatchString("foo "));
      Assert.IsTrue(matcher.MatchString("foofoo"));
      Assert.IsFalse(matcher.MatchString("oof"));
      Assert.IsFalse(matcher.MatchString("fooo"));
      Assert.IsFalse(matcher.MatchString("fo"));
      Assert.IsFalse(matcher.MatchString("oof"));
    }


    [TestMethod]
    public void SimpleAsteriskShouldWork2() {
      var matcher = new SearchPatternParser().ParsePattern("foo*");
      Assert.IsTrue(matcher.MatchString("foo"));
      Assert.IsFalse(matcher.MatchString(" foo"));
      Assert.IsTrue(matcher.MatchString("foo "));
      Assert.IsTrue(matcher.MatchString("foofoo"));
      Assert.IsFalse(matcher.MatchString("oof"));
      Assert.IsTrue(matcher.MatchString("fooo"));
      Assert.IsFalse(matcher.MatchString("fo"));
      Assert.IsFalse(matcher.MatchString("oof"));
    }


    [TestMethod]
    public void SimpleAsteriskShouldWork3() {
      var matcher = new SearchPatternParser().ParsePattern("*foo*");
      Assert.IsTrue(matcher.MatchString("foo"));
      Assert.IsTrue(matcher.MatchString(" foo"));
      Assert.IsTrue(matcher.MatchString("foo "));
      Assert.IsTrue(matcher.MatchString("foofoo"));
      Assert.IsFalse(matcher.MatchString("oof"));
      Assert.IsTrue(matcher.MatchString("fooo"));
      Assert.IsFalse(matcher.MatchString("fo"));
      Assert.IsFalse(matcher.MatchString("oof"));
    }
  }
}
