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
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtcompact;
using mtcopy;
using mtdel;
using mtfind;
using mtfindstr;
using mtinfo;
using mtmir;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.shared.CommandLine;
using tests.FileSystemHelpers;

namespace tests {
  /// <summary>
  /// Warnings, errors and plain output must behave the same way in all the tools.
  /// </summary>
  [TestClass]
  public class OutputBehaviorTest {
    private FileSystemSetup _fileSystemSetup;
    private string _root;
    private string _destination;

    private sealed class Tool {
      public string Name { get; init; }
      public Func<string, string[]> Args { get; init; }
      public Action<string[]> Run { get; init; }
    }

    private IEnumerable<Tool> Tools() {
      var fs = _fileSystemSetup.FileSystem;
      yield return new Tool { Name = "mtinfo", Args = source => new[] { source }, Run = args => new MtInfo(fs, new MtPoolFactory()).Run(args) };
      yield return new Tool { Name = "mtfind", Args = source => new[] { source, "*" }, Run = args => new MtFind(fs, new MtPoolFactory()).Run(args) };
      yield return new Tool { Name = "mtfindstr", Args = source => new[] { source, "foo" }, Run = args => new MtFindStr(fs, new MtPoolFactory()).Run(args) };
      yield return new Tool { Name = "mtcopy", Args = source => new[] { source, _destination }, Run = args => new MtCopy(fs, new MtPoolFactory()).Run(args) };
      yield return new Tool { Name = "mtmir", Args = source => new[] { source, _destination }, Run = args => new MtMirror(fs, new MtPoolFactory()).Run(args) };
      yield return new Tool { Name = "mtcompact", Args = source => new[] { source, _destination }, Run = args => new MtCompact(fs, new MtPoolFactory()).Run(args) };
      yield return new Tool { Name = "mtdel", Args = source => new[] { source, "--quiet" }, Run = args => new MtDelete(fs, new MtPoolFactory()).Run(args) };
    }

    [TestInitialize]
    public void Setup() {
      _fileSystemSetup = new FileSystemSetup();
      _root = _fileSystemSetup.Root.Path.FullName;
      _destination = _fileSystemSetup.Root.CreateDirectory("destination").Path.FullName;
    }

    [TestCleanup]
    public void Cleanup() {
      _fileSystemSetup.Dispose();
    }

    [TestMethod]
    public void MissingSourceShouldBeReportedOnStandardErrorOnly() {
      var missing = Path.Combine(_root, "missing");
      foreach (var tool in Tools()) {
        var result = Run(tool, tool.Args(missing).Concat(new[] { "-p", "none" }).ToArray());
        Assert.AreEqual(8, result.ReturnCode, tool.Name);
        Assert.IsTrue(result.Error.Contains("Error:"), tool.Name + ": " + result.Error);
        Assert.IsFalse(result.Output.Contains("missing"), tool.Name + ": " + result.Output);
      }
    }

    [TestMethod]
    public void NoErrorsShouldHideErrorsAndKeepTheReturnCode() {
      var missing = Path.Combine(_root, "missing");
      foreach (var tool in Tools()) {
        var result = Run(tool, tool.Args(missing).Concat(new[] { "--no-errors", "-p", "none" }).ToArray());
        Assert.AreEqual(8, result.ReturnCode, tool.Name);
        Assert.AreEqual("", result.Error, tool.Name);
      }
    }

    [TestMethod]
    public void InvalidArgumentsShouldOnlyWriteToStandardError() {
      foreach (var tool in Tools()) {
        var result = Run(tool, "--not-an-option");
        Assert.AreEqual(16, result.ReturnCode, tool.Name);
        Assert.AreEqual("", result.Output, tool.Name);
        Assert.IsTrue(result.Error.Contains("ERROR:"), tool.Name + ": " + result.Error);
      }
    }

    [TestMethod]
    public void HelpShouldWriteToStandardOutput() {
      foreach (var tool in Tools()) {
        var result = Run(tool, "--help");
        Assert.AreEqual(16, result.ReturnCode, tool.Name);
        Assert.AreEqual("", result.Error, tool.Name);
        Assert.IsTrue(result.Output.Contains("--no-errors") && result.Output.Contains("--warnings"), tool.Name);
      }
    }

    [TestMethod]
    public void ErrorsShouldBeDisplayedOnce() {
      if (OperatingSystem.IsWindows()) {
        Assert.Inconclusive("Test relies on Unix file permissions.");
      }

      foreach (var tool in Tools()) {
        var source = _fileSystemSetup.Root.CreateDirectory("source-" + tool.Name);
        var denied = source.CreateDirectory("denied");
        File.SetUnixFileMode(denied.Path.FullName, UnixFileMode.None);
        try {
          var result = Run(tool, tool.Args(source.Path.FullName).Concat(new[] { "-p", "none" }).ToArray());
          Assert.AreEqual(8, result.ReturnCode, tool.Name);
          // An error can have an inner exception on its own line, so look for the path.
          Assert.AreEqual(1, CountLines(result.Error, denied.Path.FullName), tool.Name + ": " + result.Error);
          Assert.AreEqual(1, CountLines(result.Output, "# of warnings"), tool.Name + ": " + result.Output);
        }
        finally {
          // mtdel removes the directory, since it is empty, even if it cannot list it
          if (Directory.Exists(denied.Path.FullName)) {
            File.SetUnixFileMode(denied.Path.FullName,
              UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
          }
        }
      }
    }

    [TestMethod]
    public void PlainOutputShouldOnlyWriteResultsToStandardOutput() {
      _fileSystemSetup.Root.CreateFile("match.txt", 10);
      foreach (var tool in Tools().Where(x => x.Name == "mtfind" || x.Name == "mtfindstr")) {
        var result = Run(tool, tool.Args(_root).Concat(new[] { "--plain-output", "--gc", "-p", "full" }).ToArray());
        Assert.AreEqual(0, result.ReturnCode, tool.Name);
        Assert.IsFalse(result.Output.Contains("GC Memory"), tool.Name + ": " + result.Output);
        Assert.IsFalse(result.Output.Contains("Statistics"), tool.Name + ": " + result.Output);
        Assert.IsTrue(result.Error.Contains("GC Memory"), tool.Name + ": " + result.Error);
      }
    }

    private static int CountLines(string text, string value) {
      return text.Split('\n').Count(line => line.Contains(value));
    }

    private static (int ReturnCode, string Output, string Error) Run(Tool tool, params string[] args) {
      using var output = new StringWriter();
      using var error = new StringWriter();
      var oldOutput = Console.Out;
      var oldError = Console.Error;
      Console.SetOut(output);
      Console.SetError(error);
      try {
        var returnCode = ProgramHelpers.RunProgram(args, () => tool.Run(args));
        return (returnCode, output.ToString(), error.ToString());
      }
      finally {
        Console.SetOut(oldOutput);
        Console.SetError(oldError);
      }
    }
  }
}
