﻿// Copyright 2015 Renaud Paquay All Rights Reserved.
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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.Utils;

namespace tests {
  [TestClass]
  public class FullPathTest {
    [TestMethod]
    public void FullPathWithAbsolutePathShouldWork() {
      var pathString = @"c:\test\test2";
      var path = new FullPath(pathString);
      Assert.AreEqual(pathString, path.FullName);
    }

    [TestMethod]
    public void FullPathWithRootPathShouldWork() {
      var pathString = @"c:\";
      var path = new FullPath(pathString);
      Assert.AreEqual(pathString, path.FullName);
      Assert.IsNull(path.Parent);
      Assert.IsTrue(path.HasTrailingSeparator);
      Assert.AreEqual(PathHelpers.RootPrefixKind.DiskPath, path.PathKind);
      Assert.AreEqual(@"c:\", path.FullName);
      Assert.AreEqual(@"c:\", path.Name);
      Assert.IsTrue(path.GetHashCode() != 0);
    }

    [TestMethod]
    public void FullPathWithTrailingSeparatorShouldWork() {
      var pathString = @"c:\test\test2\";
      var path = new FullPath(pathString);
      Assert.AreEqual(@"c:\test\test2", path.FullName);
    }

    [TestMethod]
    public void FullPathCombineShouldWork() {
      var pathString = @"c:\test\test2";
      var path = new FullPath(pathString).Combine("a");
      Assert.AreEqual(pathString + @"\a", path.FullName);
    }

    [TestMethod]
    public void FullPathCombineRelativeShouldWork() {
      var pathString = @"c:\test\test2";
      var path = new FullPath(pathString).Combine(@"a\b");
      Assert.AreEqual(pathString + @"\a\b", path.FullName);
    }

    [TestMethod]
    public void FullPathWithTrailingSeparatorCombineShouldWork() {
      var pathString = @"c:\test\test2\";
      var path = new FullPath(pathString).Combine("a");
      Assert.AreEqual(pathString + "a", path.FullName);
    }

    [TestMethod]
    public void FullPathMultipleCombineShouldWork() {
      var pathString = @"c:\test\test2";
      var path = new FullPath(pathString).Combine("a").Combine("tt");
      Assert.AreEqual(pathString + @"\a" + @"\tt", path.FullName);
    }

    [TestMethod]
    public void FullPathGetNameShouldWork() {
      var pathString = @"c:\test\test2";
      var path = new FullPath(pathString);
      Assert.AreEqual(@"test2", path.Name);
    }

    [TestMethod]
    public void FullPathParentShouldWork() {
      var pathString = @"c:\test\test2";
      var path = new FullPath(pathString);
      Assert.IsNotNull(path.Parent);
      Assert.AreEqual(@"c:\test", path.Parent?.FullName);
      Assert.IsNotNull(path.Parent?.Parent);
      Assert.AreEqual(@"c:\", path.Parent?.Parent?.FullName);
      Assert.IsNull(path.Parent?.Parent?.Parent);
    }

    [TestMethod]
    public void FullPathWithTrailingSeparatorParentShouldWork() {
      var pathString = @"c:\test\test2\";
      var path = new FullPath(pathString);
      Assert.IsNotNull(path.Parent);
      Assert.AreEqual(@"c:\test", path.Parent?.FullName);
      Assert.IsNotNull(path.Parent?.Parent);
      Assert.AreEqual(@"c:\", path.Parent?.Parent?.FullName);
      Assert.IsNull(path.Parent?.Parent?.Parent);
    }

    [TestMethod]
    public void FullPathLengthShouldWork() {
      var pathString = @"c:\test\test2";
      var path = new FullPath(pathString).Combine("a").Combine("tt");
      Assert.AreEqual(pathString + @"\a" + @"\tt", path.FullName);
      Assert.AreEqual(path.FullName.Length, path.Length);
      Assert.AreEqual(18, path.Length);
    }

    [TestMethod]
    public void FullPathCopyToShouldWork() {
      var pathString = @"c:\test\test2";
      var path = new FullPath(pathString).Combine("a").Combine("tt");
      var sb = new StringBuffer();
      path.CopyTo(sb);
      Assert.AreEqual(@"c:\test\test2\a\tt", sb.Text);
    }

    [TestMethod]
    public void FullPathToStringShouldWork() {
      var pathString = @"c:\test\test2";
      var path = new FullPath(pathString).Combine("a").Combine("tt");
      Assert.AreEqual(pathString + @"\a" + @"\tt", path.ToString());
    }

    [TestMethod]
    public void FullPathEqualShouldWork() {
      var p1 = new FullPath(@"c:\foo\bar");
      var p2 = new FullPath(@"c:\foo").Combine(@"bar");
      Assert.AreEqual(p1, p2);
      Assert.AreEqual(p1.GetHashCode(), p2.GetHashCode());
    }

    [TestMethod]
    public void LongPathShouldWork() {
      var p1 = new FullPath(@"\\?\c:\foo\bar");
      Assert.AreEqual(@"\\?\c:\foo\bar", p1.FullName);
      Assert.AreEqual(PathHelpers.RootPrefixKind.LongDiskPath, p1.PathKind);
      Assert.AreEqual("bar", p1.Name);
      Assert.AreEqual("foo", p1.Parent?.Name);
      Assert.AreEqual(@"\\?\c:\", p1.Parent?.Parent?.Name);
      Assert.AreEqual(null, p1.Parent?.Parent?.Parent);
    }

    [TestMethod]
    public void UncPathShouldWork() {
      var p1 = new FullPath(@"\\server\foo\bar");
      Assert.AreEqual(@"\\server\foo\bar", p1.FullName);
      Assert.AreEqual(PathHelpers.RootPrefixKind.UncPath, p1.PathKind);
      Assert.AreEqual("bar", p1.Name);
      Assert.AreEqual("foo", p1.Parent?.Name);
      Assert.AreEqual(@"\\server\", p1.Parent?.Parent?.Name);
      Assert.AreEqual(null, p1.Parent?.Parent?.Parent);
    }

    [TestMethod]
    public void LongUncPathShouldWork() {
      var p1 = new FullPath(@"\\?\unc\server\foo\bar");
      Assert.AreEqual(@"\\?\unc\server\foo\bar", p1.FullName);
      Assert.AreEqual(PathHelpers.RootPrefixKind.LongUncPath, p1.PathKind);
      Assert.AreEqual("bar", p1.Name);
      Assert.AreEqual("foo", p1.Parent?.Name);
      Assert.AreEqual("server", p1.Parent?.Parent?.Name);
      Assert.AreEqual(@"\\?\unc\", p1.Parent?.Parent?.Parent?.Name);
      Assert.AreEqual(null, p1.Parent?.Parent?.Parent?.Parent);
    }

    [TestMethod]
    public void FullPathEqualsShouldWork() {
      var path1 = new FullPath(@"c:\test\test2");
      var path2 = new FullPath(@"c:\test");
      Assert.IsFalse(path1.Equals(path2));
      Assert.IsFalse(path2.Equals(path1));
      Assert.IsFalse(Object.Equals(path1, path2));
      Assert.AreNotEqual(path1.GetHashCode(), path2.GetHashCode());
    }

    [TestMethod]
    public void FullPathEqualsShouldWork2() {
      var path1 = new FullPath(@"c:\test\test2");
      var path2 = new FullPath(@"c:\test").Combine("test2");
      Assert.IsTrue(path1.Equals(path2));
      Assert.IsTrue(path2.Equals(path1));
      Assert.IsTrue(Object.Equals(path1, path2));
      Assert.AreEqual(path1.GetHashCode(), path2.GetHashCode());
    }

    [TestMethod]
    public void FullPathCompareToShouldWork() {
      var path1 = new FullPath(@"c:\test\foo\bar\blah");
      var path2 = new FullPath(@"c:\test\b");
      Assert.IsTrue(path1.CompareTo(path2) > 0);
      Assert.IsTrue(path2.CompareTo(path1) < 0);
    }

    [TestMethod]
    public void FullPathCompareToShouldWork2() {
      var path1 = new FullPath(@"c:\test\b");
      var path2 = new FullPath(@"c:\test\foo\bar\blah");
      Assert.IsTrue(path1.CompareTo(path2) < 0);
      Assert.IsTrue(path2.CompareTo(path1) > 0);
    }

    [TestMethod]
    public void FullPathCompareToShouldWork3() {
      var path1 = new FullPath(@"c:\test\test2");
      var path2 = new FullPath(@"c:\testaaaa");
      Assert.IsTrue(path1.CompareTo(path2) < 0);
      Assert.IsTrue(path2.CompareTo(path1) > 0);
    }

    [TestMethod]
    public void FullPathCompareToShouldWork4() {
      var path1 = new FullPath(@"c:\test\test2");
      var path2 = new FullPath(@"c:\test").Combine("test2");
      Assert.IsTrue(path1.CompareTo(path2) == 0);
      Assert.IsTrue(path2.CompareTo(path1) == 0);
    }

    [TestMethod]
    public void FullPathShouldBeCaseInsensitiveShouldWork() {
      var path1 = new FullPath(@"c:\test");
      var path2 = new FullPath(@"c:\TEST");
      Assert.IsTrue(path1.Equals(path2));
      Assert.AreEqual(path1.GetHashCode(), path2.GetHashCode());
      Assert.AreEqual(0, path1.CompareTo(path2));
    }
  }
}
