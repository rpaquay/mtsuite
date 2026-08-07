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
using mtsuite.CoreFileSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests {
  [TestClass]
  public class PathHelpersTest {
    [TestMethod]
    public void IsPathAbsoluteTest() {
      Assert.IsTrue(PathHelpers.IsPathAbsolute(@"c:\"));
      Assert.IsTrue(PathHelpers.IsPathAbsolute(@"c:\gfdfg"));
      Assert.IsTrue(PathHelpers.IsPathAbsolute(@"\\gfdfg"));
      Assert.IsTrue(PathHelpers.IsPathAbsolute(@"\\gfdfg\fd"));
      Assert.IsTrue(PathHelpers.IsPathAbsolute(@"/"));
      Assert.IsTrue(PathHelpers.IsPathAbsolute(@"/test"));

      Assert.IsFalse(PathHelpers.IsPathAbsolute(@"c:"));
      Assert.IsFalse(PathHelpers.IsPathAbsolute(@"c:fdsdf"));
      Assert.IsFalse(PathHelpers.IsPathAbsolute(@"\fdsdf"));
      Assert.IsFalse(PathHelpers.IsPathAbsolute(@"test"));
    }

    [TestMethod]
    public void NormalizePathTest() {
      if (OperatingSystem.IsWindows()) {
        Assert.AreEqual(@"c:\", PathHelpers.NormalizePath(@"c:\test\.."));
        Assert.AreEqual(@"\\server\share", PathHelpers.NormalizePath(@"\\server\share"));
        Assert.AreEqual(@"\\server\share", PathHelpers.NormalizePath(@"\\server\share\."));
        Assert.AreEqual(@"\\server", PathHelpers.NormalizePath(@"\\server\share\.."));
      } else {
        Assert.AreEqual(@"/", PathHelpers.NormalizePath(@"/test/.."));
        Assert.AreEqual(@"/server/share", PathHelpers.NormalizePath(@"/server/share"));
        Assert.AreEqual(@"/server/share", PathHelpers.NormalizePath(@"/server/share/."));
        Assert.AreEqual(@"/server", PathHelpers.NormalizePath(@"/server/share/.."));
      }
    }

    [TestMethod]
    public void GetParentTest() {
      if (OperatingSystem.IsWindows()) {
        Assert.AreEqual(@"c:\", PathHelpers.GetParent(@"c:\test"));
        Assert.AreEqual(@"\\server\", PathHelpers.GetParent(@"\\server\share"));
        Assert.AreEqual(@"c:\", PathHelpers.GetParent(@"c:\test\"));
        Assert.AreEqual(@"\\server\", PathHelpers.GetParent(@"\\server\share\"));
        Assert.AreEqual(null, PathHelpers.GetParent(@"c:\"));
        Assert.AreEqual(null, PathHelpers.GetParent(@"\\server\"));
      } else {
        Assert.AreEqual(@"/", PathHelpers.GetParent(@"/test"));
        Assert.AreEqual(@"/server/", PathHelpers.GetParent(@"/server/share"));
        Assert.AreEqual(@"/", PathHelpers.GetParent(@"/test/"));
        Assert.AreEqual(@"/server/", PathHelpers.GetParent(@"/server/share/"));
        Assert.AreEqual(null, PathHelpers.GetParent(@"/"));
      }
    }

    [TestMethod]
    public void GetFileNameTest() {
      if (OperatingSystem.IsWindows()) {
        Assert.AreEqual(@"test", PathHelpers.GetName(@"c:\test"));
        Assert.AreEqual(@"share", PathHelpers.GetName(@"\\server\share"));
        Assert.AreEqual(@"test", PathHelpers.GetName(@"c:\test\"));
        Assert.AreEqual(@"share", PathHelpers.GetName(@"\\server\share\"));
        Assert.AreEqual(null, PathHelpers.GetName(@"c:\"));
        Assert.AreEqual(null, PathHelpers.GetName(@"\\server\"));
      } else {
        Assert.AreEqual(@"test", PathHelpers.GetName(@"/test"));
        Assert.AreEqual(@"share", PathHelpers.GetName(@"/server/share"));
        Assert.AreEqual(@"test", PathHelpers.GetName(@"/test/"));
        Assert.AreEqual(@"share", PathHelpers.GetName(@"/server/share/"));
        Assert.AreEqual(null, PathHelpers.GetName(@"/"));
      }
    }

    [TestMethod]
    public void NormalizeUserInputPathTest() {
      if (OperatingSystem.IsWindows()) {
        Assert.AreEqual(@"c:\test", PathHelpers.NormalizeUserInputPath(@"c:\", @"c:\test"));
        Assert.AreEqual(@"c:\test", PathHelpers.NormalizeUserInputPath(@"c:\", @"\test"));
        Assert.AreEqual(@"c:\test", PathHelpers.NormalizeUserInputPath(@"c:\", @"test\"));
        Assert.AreEqual(@"c:\", PathHelpers.NormalizeUserInputPath(@"c:\", @"c:"));
        Assert.AreEqual(@"d:\", PathHelpers.NormalizeUserInputPath(@"c:\", @"d:"));
      } else {
        Assert.AreEqual(@"/test", PathHelpers.NormalizeUserInputPath(@"/", @"/test"));
        Assert.AreEqual(@"/test", PathHelpers.NormalizeUserInputPath(@"/", @"test/"));
      }
    }
  }
}
