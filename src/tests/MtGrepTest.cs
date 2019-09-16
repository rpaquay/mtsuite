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

using System.IO;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using mtsuite.shared.CommandLine;

using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class MtGrepTest {
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
    public void SearchStreamShouldFindEntries() {
      var value = "foo bar foo\n\n\nbar foo";
      byte[] byteArray = Encoding.ASCII.GetBytes(value);
      using (var stream = new MemoryStream(byteArray)) {
        using (var reader = new StreamReader(stream)) {
          var result = new mtgrep.GrepStream().Search(reader, "foo".ToCharArray());

          Assert.AreEqual(3, result.Count);

          Assert.AreEqual(1, result[0].LineNumber);
          Assert.AreEqual(1, result[0].ColumnNumber);

          Assert.AreEqual(1, result[1].LineNumber);
          Assert.AreEqual(9, result[1].ColumnNumber);

          Assert.AreEqual(4, result[2].LineNumber);
          Assert.AreEqual(5, result[2].ColumnNumber);
        }
      }
    }
  }
}
