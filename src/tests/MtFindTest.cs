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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtfind;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.shared.CommandLine;
using mtsuite.shared;
using mtsuite.CoreFileSystem;
using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class MtFindTest {
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
    public void MtFindShouldThrowWithNonExistingFolder() {
      var mtfind = new MtFind(_fileSystemSetup.FileSystem, new MtPoolFactory());
      mtfind.Run(new string[] {
        "fake",
        "-name",
        "foo"
      });
    }

    [TestMethod]
    public void MtFindShouldFindMatchingFilesAndTriggerMatchEvent() {
      _fileSystemSetup.Root.CreateFile("file1.txt", 10);
      _fileSystemSetup.Root.CreateFile("file2.log", 20);
      var sub = _fileSystemSetup.Root.CreateDirectory("sub");
      sub.CreateFile("file3.txt", 30);

      var mtfind = new MtFind(_fileSystemSetup.FileSystem, new MtPoolFactory());
      var matches = mtfind.DoFind(_fileSystemSetup.Root.Path, "*.txt", isPlainOutput: false, followLinks: false, includeDir: false);

      Assert.AreEqual(2, matches.Count);
      Assert.IsTrue(matches.Exists(m => m.Name == "file1.txt"));
      Assert.IsTrue(matches.Exists(m => m.Name == "file3.txt"));
    }

    [TestMethod]
    public void MtFindPlainOutputShouldDisplayEachMatchOnce() {
      _fileSystemSetup.Root.CreateFile("file1.txt", 10);
      _fileSystemSetup.Root.CreateFile("file2.log", 20);
      var sub = _fileSystemSetup.Root.CreateDirectory("sub");
      sub.CreateFile("file3.txt", 30);

      var output = RunAndCaptureOutput("--plain-output", "-p", "none", _fileSystemSetup.Root.Path.FullName, "-name", "*.txt");

      var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
      Assert.AreEqual(2, lines.Length, output);
      CollectionAssert.Contains(lines, _fileSystemSetup.Root.Path.Combine("file1.txt").FullName);
      CollectionAssert.Contains(lines, sub.Path.Combine("file3.txt").FullName);
    }

    [TestMethod]
    public void MtFindShouldNotFollowLinksByDefault() {
      Assert.IsFalse(new MtFindArguments(new[] { "-name", "x" }).Values.FollowLinks);
      Assert.IsTrue(new MtFindArguments(new[] { "--follow-links", "-name", "x" }).Values.FollowLinks);
    }

    [TestMethod]
    public void MtFindShouldDisplayErrorsUnlessDisabled() {
      if (OperatingSystem.IsWindows()) {
        Assert.Inconclusive("Test relies on Unix file permissions.");
      }

      var denied = _fileSystemSetup.Root.CreateDirectory("denied");
      var rootName = _fileSystemSetup.Root.Path.FullName;
      System.IO.File.SetUnixFileMode(denied.Path.FullName, System.IO.UnixFileMode.None);
      try {
        var withErrors = RunAndCaptureErrors("--plain-output", "-p", "none", rootName, "-name", "*.txt");
        Assert.IsTrue(withErrors.Contains("Error:"), withErrors);

        var withoutErrors = RunAndCaptureErrors("--no-errors", "-p", "none", rootName, "-name", "*.txt");
        Assert.AreEqual("", withoutErrors);
      }
      finally {
        System.IO.File.SetUnixFileMode(denied.Path.FullName,
          System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute);
      }
    }

    private string RunAndCaptureOutput(params string[] args) {
      using var writer = new System.IO.StringWriter();
      var oldOut = Console.Out;
      Console.SetOut(writer);
      try {
        new MtFind(_fileSystemSetup.FileSystem, new MtPoolFactory()).Run(args);
      }
      finally {
        Console.SetOut(oldOut);
      }
      return writer.ToString();
    }

    private string RunAndCaptureErrors(params string[] args) {
      using var writer = new System.IO.StringWriter();
      var oldError = Console.Error;
      Console.SetError(writer);
      try {
        // The directory that cannot be read results in a non-zero exit code
        Assert.ThrowsException<CommandLineReturnValueException>(() =>
          new MtFind(_fileSystemSetup.FileSystem, new MtPoolFactory()).Run(args));
      }
      finally {
        Console.SetError(oldError);
      }
      return writer.ToString();
    }

    [TestMethod]
    public void MtFindArgumentsShouldParsePositionalDirectoryAndPattern() {
      // 1. Omitted directory
      {
        var args = new MtFindArguments(new[] { "-name", "mypattern" });
        Assert.AreEqual(Environment.CurrentDirectory, args.Values.Directory);
        Assert.AreEqual("mypattern", args.Values.Pattern);
      }

      // 2. Provided directory and pattern
      {
        var args = new MtFindArguments(new[] { "my/dir/path", "-name", "mypattern" });
        Assert.AreEqual("my/dir/path", args.Values.Directory);
        Assert.AreEqual("mypattern", args.Values.Pattern);
      }

      // 3. Flags and omitted directory
      {
        var args = new MtFindArguments(new[] { "--plain-output", "-name", "mypattern" });
        Assert.IsTrue(args.Values.PlainOutput);
        Assert.AreEqual(Environment.CurrentDirectory, args.Values.Directory);
        Assert.AreEqual("mypattern", args.Values.Pattern);
      }

      // 4. Flags and provided directory and pattern
      {
        var args = new MtFindArguments(new[] { "--plain-output", "my/dir/path", "-name", "mypattern" });
        Assert.IsTrue(args.Values.PlainOutput);
        Assert.AreEqual("my/dir/path", args.Values.Directory);
        Assert.AreEqual("mypattern", args.Values.Pattern);
      }
    }

    [TestMethod]
    public void FindProgressMonitorShouldSupportSingleLineProgress() {
      var monitor = new FindProgressMonitor();
      monitor.ProgressMode = ProgressMode.Line;
      monitor.IsAnsiSupported = true;
      
      using (var writer = new System.IO.StringWriter()) {
        var oldOut = Console.Out;
        Console.SetOut(writer);
        try {
          monitor.Start();
          monitor.Stop();
        } finally {
          Console.SetOut(oldOut);
        }
        
        var output = writer.ToString();
        
        Assert.IsFalse(output.Contains('\n'), "Single-line progress should not contain newlines");
        Assert.IsTrue(output.Contains("Elapsed time"), "Should contain elapsed time field");
        Assert.IsTrue(output.Contains("CPU time"), "Should contain CPU time field");
      }
    }

    [TestMethod]
    public void FindProgressMonitorShouldSupportDefaultProgressModeWithoutThreads() {
      var monitor = new FindProgressMonitor();
      monitor.ProgressMode = ProgressMode.Default;
      monitor.IsAnsiSupported = true;
      
      var entry = new FileSystemEntry(
        new FullPath("/foo/bar"),
        new FileSystemEntryData(System.IO.FileAttributes.Directory, 0, 0)
      );
      monitor.OnDirectoryTraversing(entry);
      
      using (var writer = new System.IO.StringWriter()) {
        var oldOut = Console.Out;
        Console.SetOut(writer);
        try {
          monitor.Start();
          monitor.Stop();
        } finally {
          Console.SetOut(oldOut);
        }
        
        var output = writer.ToString();
        
        Assert.IsTrue(output.Contains('\n'), "Default progress should contain newlines");
        Assert.IsTrue(output.Contains("Elapsed time"), "Should contain elapsed time field");
        Assert.IsFalse(output.Contains("Threads:"), "Default progress should not contain thread progress");
        Assert.IsFalse(output.Contains("/foo/bar"), "Default progress should not show traversing directories");
      }
    }

    [TestMethod]
    public void FindProgressMonitorShouldSupportFullProgressModeWithThreads() {
      var monitor = new FindProgressMonitor();
      monitor.ProgressMode = ProgressMode.Full;
      monitor.IsAnsiSupported = true;
      
      var entry = new FileSystemEntry(
        new FullPath("/foo/bar"),
        new FileSystemEntryData(System.IO.FileAttributes.Directory, 0, 0)
      );
      monitor.OnDirectoryTraversing(entry);
      
      using (var writer = new System.IO.StringWriter()) {
        var oldOut = Console.Out;
        Console.SetOut(writer);
        try {
          monitor.Start();
          monitor.Stop();
        } finally {
          Console.SetOut(oldOut);
        }
        
        var output = writer.ToString();
        
        Assert.IsTrue(output.Contains('\n'), "Full progress should contain newlines");
        Assert.IsTrue(output.Contains("Threads:"), "Full progress should contain thread progress");
        Assert.IsTrue(output.Contains("/foo/bar"), "Full progress should show traversing directories");
      }
    }
  }
}
