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
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    public void MtFindStrPlainOutputShouldDisplayEachMatchOnce() {
      var file1 = _fileSystemSetup.Root.CreateFile("a.txt", 10);
      System.IO.File.WriteAllText(file1.Path.FullName, "hello world\nsecond hello");

      using var writer = new System.IO.StringWriter();
      var oldOut = Console.Out;
      Console.SetOut(writer);
      try {
        new MtFindStr(_fileSystemSetup.FileSystem, new MtPoolFactory()).Run(new[] {
          "--plain-output", "-p", "none", _fileSystemSetup.Root.Path.FullName, "hello"
        });
      }
      finally {
        Console.SetOut(oldOut);
      }

      var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
      CollectionAssert.AreEqual(new[] { file1.Path.FullName + ":1:1", file1.Path.FullName + ":2:8" }, lines);
    }

    [TestMethod]
    [Timeout(60000)]
    public void MtFindStrShouldSearchAllFilesOfLargeDirectories() {
      var sub = _fileSystemSetup.Root.CreateDirectory("sub");
      var expected = new List<string>();
      for (var i = 0; i < 1000; i++) {
        var file = (i % 2 == 0 ? _fileSystemSetup.Root : sub).CreateFile("file" + i + ".txt", 10);
        var isMatch = i % 3 == 0;
        System.IO.File.WriteAllText(file.Path.FullName, isMatch ? "hello world" : "other text");
        if (isMatch) {
          expected.Add(file.Path.FullName);
        }
      }

      var results = new MtFindStr(_fileSystemSetup.FileSystem, new MtPoolFactory())
        .DoFindStr(_fileSystemSetup.Root.Path, new[] { "*.txt" }, "hello", isPlainOutput: false, followLinks: false);

      CollectionAssert.AreEquivalent(expected, results.Select(x => x.Path.FullName).ToList());
    }

    [TestMethod]
    [Timeout(60000)]
    public void MtFindStrShouldCompleteWithoutAnyFileToSearch() {
      var results = new MtFindStr(_fileSystemSetup.FileSystem, new MtPoolFactory())
        .DoFindStr(_fileSystemSetup.Root.Path, new[] { "*.txt" }, "hello", isPlainOutput: false, followLinks: false);

      Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    [Timeout(60000)]
    public void MtFindStrShouldCompleteAndReportErrorWhenFileCannotBeRead() {
      if (OperatingSystem.IsWindows()) {
        Assert.Inconclusive("Test relies on Unix file permissions.");
      }

      var readable = _fileSystemSetup.Root.CreateFile("readable.txt", 10);
      System.IO.File.WriteAllText(readable.Path.FullName, "hello world");
      var denied = _fileSystemSetup.Root.CreateFile("denied.txt", 10);
      System.IO.File.WriteAllText(denied.Path.FullName, "hello world");
      System.IO.File.SetUnixFileMode(denied.Path.FullName, System.IO.UnixFileMode.None);

      using var writer = new System.IO.StringWriter();
      var oldError = Console.Error;
      Console.SetError(writer);
      try {
        var results = new MtFindStr(_fileSystemSetup.FileSystem, new MtPoolFactory())
          .DoFindStr(_fileSystemSetup.Root.Path, new[] { "*.txt" }, "hello", isPlainOutput: false, followLinks: false);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(readable.Path.FullName, results[0].Path.FullName);
        Assert.IsTrue(writer.ToString().Contains("denied.txt"), writer.ToString());
      }
      finally {
        Console.SetError(oldError);
        System.IO.File.SetUnixFileMode(denied.Path.FullName, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite);
      }
    }

    [TestMethod]
    public void MtFindStrShouldNotFollowLinksByDefault() {
      Assert.IsFalse(new MtFindStrArguments(new[] { "x" }).Values.FollowLinks);
      Assert.IsTrue(new MtFindStrArguments(new[] { "--follow-links", "x" }).Values.FollowLinks);
      Assert.IsTrue(new MtFindStrArguments(new[] { "-L", "x" }).Values.FollowLinks);
      Assert.IsTrue(new MtFindStrArguments(new[] { "-fl", "x" }).Values.FollowLinks);
    }

    [TestMethod]
    public void MtFindStrShouldReportCorrectPositionsAcrossBufferBoundaries() {
      const string needle = "needle";
      const int bufferSize = FileIOByteArrayPool.BufferSize;

      // A multi-byte file with a match crossing the end of the first buffer, on a line that
      // starts in the middle of the buffer, then matches in the following buffers.
      var text = new StringBuilder();
      var line = "héllo wörld 文字 " + needle + " end\n";
      var lineCount = (bufferSize - 2000) / Encoding.UTF8.GetByteCount(line);
      text.Insert(0, line, lineCount);
      text.Append("é 文");
      text.Append('x', bufferSize - 3 - Encoding.UTF8.GetByteCount(text.ToString()));
      text.Append(needle + " tail\n");
      text.Insert(text.Length, "no match on this line é\n", 100000);
      text.Append("last line é " + needle);

      var content = text.ToString();
      var file = _fileSystemSetup.Root.CreateFile("large.txt", 10);
      System.IO.File.WriteAllText(file.Path.FullName, content, new UTF8Encoding(false));

      var results = new MtFindStr(_fileSystemSetup.FileSystem, new MtPoolFactory())
        .DoFindStr(_fileSystemSetup.Root.Path, new[] { "*.txt" }, needle, isPlainOutput: false, followLinks: false);

      // Compute the expected positions with a naive scan (all characters are in the BMP).
      var expected = new List<(int Line, int Column)>();
      var lineNumber = 1;
      var lineStart = 0;
      var scanned = 0;
      for (var index = content.IndexOf(needle, StringComparison.Ordinal); index >= 0;
           index = content.IndexOf(needle, index + needle.Length, StringComparison.Ordinal)) {
        for (; scanned < index; scanned++) {
          if (content[scanned] == '\n') {
            lineNumber++;
            lineStart = scanned + 1;
          }
        }
        expected.Add((lineNumber, index - lineStart + 1));
      }

      Assert.AreEqual(1, results.Count);
      Assert.IsTrue(Encoding.UTF8.GetByteCount(content) > 2 * bufferSize);
      CollectionAssert.AreEqual(expected, results[0].Entries.Select(e => (e.LineNumber, e.ColumnNumber)).ToList());
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
