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
      foreach (var option in new[] { SearchPatternParser.Options.Default, SearchPatternParser.Options.Optimize }) {
        var matcher = new SearchPatternParser().ParsePattern("foo", option);
        Assert.IsTrue(matcher.MatchString("foo"));
        Assert.IsFalse(matcher.MatchString(" foo"));
        Assert.IsFalse(matcher.MatchString("foo "));
        Assert.IsFalse(matcher.MatchString("foofoo"));
        Assert.IsFalse(matcher.MatchString("oof"));
        Assert.IsFalse(matcher.MatchString("fooo"));
        Assert.IsFalse(matcher.MatchString("fo"));
        Assert.IsFalse(matcher.MatchString("oof"));
      }
    }

    [TestMethod]
    public void SimpleQuestionMarkShouldWork() {
      foreach (var option in new[] { SearchPatternParser.Options.Default, SearchPatternParser.Options.Optimize }) {
        var matcher = new SearchPatternParser().ParsePattern("f?o", option);
        Assert.IsTrue(matcher.MatchString("foo"));
        Assert.IsFalse(matcher.MatchString(" foo"));
        Assert.IsFalse(matcher.MatchString("foo "));
        Assert.IsFalse(matcher.MatchString("foofoo"));
        Assert.IsFalse(matcher.MatchString("oof"));
        Assert.IsFalse(matcher.MatchString("fooo"));
        Assert.IsFalse(matcher.MatchString("fo"));
        Assert.IsFalse(matcher.MatchString("oof"));
      }
    }

    [TestMethod]
    public void SimpleQuestionMarkShouldWork2() {
      foreach (var option in new[] { SearchPatternParser.Options.Default, SearchPatternParser.Options.Optimize }) {
        var matcher = new SearchPatternParser().ParsePattern("f?o?", option);
        Assert.IsFalse(matcher.MatchString("foo"));
        Assert.IsFalse(matcher.MatchString(" foo"));
        Assert.IsTrue(matcher.MatchString("foo "));
        Assert.IsTrue(matcher.MatchString("fbob"));
        Assert.IsFalse(matcher.MatchString("foofoo"));
        Assert.IsFalse(matcher.MatchString("oof"));
        Assert.IsTrue(matcher.MatchString("fooo"));
        Assert.IsFalse(matcher.MatchString("fo"));
        Assert.IsFalse(matcher.MatchString("oof"));
      }
    }

    [TestMethod]
    public void SimpleAsteriskShouldWork() {
      foreach (var option in new[] { SearchPatternParser.Options.Default, SearchPatternParser.Options.Optimize }) {
        var matcher = new SearchPatternParser().ParsePattern("*foo", option);
        Assert.IsTrue(matcher.MatchString("foo"));
        Assert.IsTrue(matcher.MatchString(" foo"));
        Assert.IsFalse(matcher.MatchString("foo "));
        Assert.IsTrue(matcher.MatchString("foofoo"));
        Assert.IsFalse(matcher.MatchString("oof"));
        Assert.IsFalse(matcher.MatchString("fooo"));
        Assert.IsFalse(matcher.MatchString("fo"));
        Assert.IsFalse(matcher.MatchString("oof"));
      }
    }


    [TestMethod]
    public void SimpleAsteriskShouldWork2() {
      foreach (var option in new[] { SearchPatternParser.Options.Default, SearchPatternParser.Options.Optimize }) {
        var matcher = new SearchPatternParser().ParsePattern("foo*", option);
        Assert.IsTrue(matcher.MatchString("foo"));
        Assert.IsFalse(matcher.MatchString(" foo"));
        Assert.IsTrue(matcher.MatchString("foo "));
        Assert.IsTrue(matcher.MatchString("foofoo"));
        Assert.IsFalse(matcher.MatchString("oof"));
        Assert.IsTrue(matcher.MatchString("fooo"));
        Assert.IsFalse(matcher.MatchString("fo"));
        Assert.IsFalse(matcher.MatchString("oof"));
      }
    }


    [TestMethod]
    public void SimpleAsteriskShouldWork3() {
      foreach (var option in new[] { SearchPatternParser.Options.Default, SearchPatternParser.Options.Optimize }) {
        var matcher = new SearchPatternParser().ParsePattern("*foo*", option);
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

    [TestMethod]
    public void DoubleAsteriskShouldWork() {
      foreach (var option in new[] { SearchPatternParser.Options.Default, SearchPatternParser.Options.Optimize }) {
        var matcher = new SearchPatternParser().ParsePattern("*foo*bar", option);
        Assert.IsTrue(matcher.MatchString("foobar"));
        Assert.IsTrue(matcher.MatchString(" foo bar"));
        Assert.IsFalse(matcher.MatchString(" foo bar "));
        Assert.IsTrue(matcher.MatchString("foofoobar"));
        Assert.IsFalse(matcher.MatchString("foobarfoo"));
        Assert.IsFalse(matcher.MatchString("barfoo"));
        Assert.IsFalse(matcher.MatchString("oofbar"));
        Assert.IsTrue(matcher.MatchString("foooobar"));
        Assert.IsFalse(matcher.MatchString("foooobarrr"));
        Assert.IsFalse(matcher.MatchString("foba"));
        Assert.IsFalse(matcher.MatchString("oofbar"));
      }
    }

    [TestMethod]
    public void TripleAsteriskShouldWork() {
      foreach (var option in new[] { SearchPatternParser.Options.Default, SearchPatternParser.Options.Optimize }) {
        var matcher = new SearchPatternParser().ParsePattern("*foo*bar*", option);
        Assert.IsTrue(matcher.MatchString("foobar"));
        Assert.IsTrue(matcher.MatchString(" foo bar"));
        Assert.IsTrue(matcher.MatchString(" foo bar "));
        Assert.IsTrue(matcher.MatchString("foofoobar"));
        Assert.IsTrue(matcher.MatchString("foobarfoo"));
        Assert.IsFalse(matcher.MatchString("barfoo"));
        Assert.IsFalse(matcher.MatchString("oofbar"));
        Assert.IsTrue(matcher.MatchString("foooobar"));
        Assert.IsTrue(matcher.MatchString("foooobarrr"));
        Assert.IsFalse(matcher.MatchString("foba"));
        Assert.IsFalse(matcher.MatchString("oofbar"));
      }
    }
  }
}
