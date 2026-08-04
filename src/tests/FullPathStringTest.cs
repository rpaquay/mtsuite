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
#nullable enable

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.Utils;

namespace tests;

[TestClass]
public class FullPathStringTest
{
    [TestMethod]
    public void FullPathString_BasicProperties()
    {
        string rootPath = OperatingSystem.IsWindows() ? @"C:\root\sub" : "/root/sub";
        var path = new FullPath(rootPath);

        Assert.AreEqual(rootPath, path.FullName);
        Assert.AreEqual("sub", path.Name);
        Assert.IsTrue(path.Name.SequenceEqual("sub".AsSpan()));
        Assert.AreEqual(rootPath.Length, path.Length);
        Assert.IsFalse(path.IsEmpty);
        Assert.IsFalse(path.HasTrailingSeparator);
        Assert.IsNotNull(path.Parent);
        Assert.AreEqual(OperatingSystem.IsWindows() ? @"C:\root" : "/root", path.Parent.Value.FullName);
    }

    [TestMethod]
    public void FullPathString_CombineOverloads()
    {
        string rootPath = OperatingSystem.IsWindows() ? @"C:\root" : "/root";
        var root = new FullPath(rootPath);

        // String overload
        var child1 = root.Combine("dir1");
        Assert.AreEqual(OperatingSystem.IsWindows() ? @"C:\root\dir1" : "/root/dir1", child1.FullName);
        Assert.AreEqual("dir1", child1.Name);

        // Span overload
        var child2 = root.Combine("dir2".AsSpan());
        Assert.AreEqual(OperatingSystem.IsWindows() ? @"C:\root\dir2" : "/root/dir2", child2.FullName);
        Assert.AreEqual("dir2", child2.Name);
        Assert.IsNotNull(child2.Parent);
        Assert.AreEqual(root, child2.Parent.Value);

        // StringSlice overload
        var factory = new StringSliceFactory();
        var slice = factory.Create("file.txt");
        var child3 = child1.Combine(slice);
        Assert.AreEqual(OperatingSystem.IsWindows() ? @"C:\root\dir1\file.txt" : "/root/dir1/file.txt", child3.FullName);
        Assert.AreEqual("file.txt", child3.Name);
        Assert.AreEqual(child1, child3.Parent!.Value);
    }

    [TestMethod]
    public void FullPathString_EqualityAndComparison()
    {
        string p1 = OperatingSystem.IsWindows() ? @"C:\a\b" : "/a/b";
        string p2 = OperatingSystem.IsWindows() ? @"C:\a\b" : "/a/b";
        string p3 = OperatingSystem.IsWindows() ? @"C:\a\c" : "/a/c";

        var path1 = new FullPath(p1);
        var path2 = new FullPath(p2);
        var path3 = new FullPath(p3);

        Assert.IsTrue(path1.Equals(path2));
        Assert.IsTrue(path1 == path2);
        Assert.IsFalse(path1 != path2);
        Assert.AreEqual(path1.GetHashCode(), path2.GetHashCode());

        Assert.IsFalse(path1.Equals(path3));
        Assert.IsTrue(path1 != path3);
        Assert.IsTrue(path1.CompareTo(path3) < 0);
    }

    [TestMethod]
    public void FullPathString_InteropsWithFullPath()
    {
        string rootPath = OperatingSystem.IsWindows() ? @"C:\root\sub" : "/root/sub";
        var fullPath = new FullPath(rootPath);
        var fullPathString = (FullPath)fullPath;

        Assert.AreEqual(fullPath.FullName, fullPathString.FullName);
        Assert.AreEqual(fullPath.Name, fullPathString.Name);

        var convertedBack = (FullPath)fullPathString;
        Assert.AreEqual(fullPath, convertedBack);
    }
}
