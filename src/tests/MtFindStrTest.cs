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

using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtfindstr;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.shared.CommandLine;
using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class MtFindStrTest {
    private FileSystemSetup _fileSystemSetup;

    [TestInitialize]
    public void Setup() {
      _fileSystemSetup = new FileSystemSetup();
    }

    [TestCleanup]
    public void Cleanup() {
      _fileSystemSetup.Dispose();
      _fileSystemSetup = null;
    }

    [TestMethod]
    [ExpectedException(typeof(CommandLineReturnValueException))]
    public void MtFindStrShouldThrowWithNonExistingFolder() {
      var findStr = new MtFindStr(_fileSystemSetup.FileSystem, new MtPoolFactory());
      findStr.Run(new string[] {
        _fileSystemSetup.Root.Path.Combine("fake").FullName,
        "foobar2"
      });
    }

    [TestMethod]
    public void MtFindStrShouldFindMatchesAndTriggerMatchEvent() {
      var file1 = _fileSystemSetup.Root.CreateFile("a.txt", 10);
      System.IO.File.WriteAllText(file1.Path.FullName, "hello world\nsecond hello");

      var findStr = new MtFindStr(_fileSystemSetup.FileSystem, new MtPoolFactory());
      var results = findStr.DoFindStr(_fileSystemSetup.Root.Path, new[] { "*.txt" }, "hello", isPlainOutput: false, followLinks: false);

      Assert.AreEqual(1, results.Count);
      Assert.AreEqual(2, results[0].Entries.Count);
    }

    [TestMethod]
    public void MtFindStrArgumentsShouldParsePositionalAndNamedArguments() {
      // 1. Omitted directory and omitted file pattern (defaults to "*")
      {
        var args = new MtFindStrArguments(new[] { "my search string" });
        Assert.AreEqual(System.IO.Directory.GetCurrentDirectory(), args.Values.Directory);
        Assert.AreEqual("my search string", args.Values.SearchPattern);
        Assert.AreEqual(1, args.Values.FileNamePatterns.Count);
        Assert.AreEqual("*", args.Values.FileNamePatterns[0]);
      }

      // 2. Provided directory, omitted file pattern, provided search pattern
      {
        var args = new MtFindStrArguments(new[] { "my/dir/path", "my search string" });
        Assert.AreEqual("my/dir/path", args.Values.Directory);
        Assert.AreEqual("my search string", args.Values.SearchPattern);
        Assert.AreEqual(1, args.Values.FileNamePatterns.Count);
        Assert.AreEqual("*", args.Values.FileNamePatterns[0]);
      }

      // 3. Provided directory, provided file pattern, provided search pattern
      {
        var args = new MtFindStrArguments(new[] { "my/dir/path", "-name", "*.txt", "my search string" });
        Assert.AreEqual("my/dir/path", args.Values.Directory);
        Assert.AreEqual("my search string", args.Values.SearchPattern);
        Assert.AreEqual(1, args.Values.FileNamePatterns.Count);
        Assert.AreEqual("*.txt", args.Values.FileNamePatterns[0]);
      }
    }
  }
}
