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
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using mtfindstr;
using mtsuite.CoreFileSystem.ObjectPool;
using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class FindStrSearchTest {
    private FileSystemSetup _fileSystemSetup;
    private MtPoolFactory _poolFactory;
    private IPool<IList<FindStrEntry>> _listPool;

    [TestInitialize]
    public void Setup() {
      _fileSystemSetup = new FileSystemSetup();
      _poolFactory = new MtPoolFactory();
      _listPool = _poolFactory.Create<IList<FindStrEntry>>("FindStrEntries", () => new List<FindStrEntry>(), static list => list.Clear());
    }

    [TestCleanup]
    public void Cleanup() {
      _fileSystemSetup.Dispose();
      _fileSystemSetup = null;
      _poolFactory = null;
      _listPool = null;
    }

    [TestMethod]
    public void SearchStreamShouldFindEntries() {
      var value = "foo bar foo\n\n\nbar foo";
      byte[] byteArray = Encoding.ASCII.GetBytes(value);
      using (var stream = new MemoryStream(byteArray)) {
        using (var reader = new StreamReader(stream)) {
          using (var result = new FindStrStream().Search(reader, "foo".ToCharArray(), _listPool)) {
            Assert.AreEqual(3, result.Item.Count);

            Assert.AreEqual(1, result.Item[0].LineNumber);
            Assert.AreEqual(1, result.Item[0].ColumnNumber);

            Assert.AreEqual(1, result.Item[1].LineNumber);
            Assert.AreEqual(9, result.Item[1].ColumnNumber);

            Assert.AreEqual(4, result.Item[2].LineNumber);
            Assert.AreEqual(5, result.Item[2].ColumnNumber);
          }
        }
      }
    }

    [TestMethod]
    public void SearchStreamShouldFindEntriesAcrossBufferBoundaries() {
      var value = "foo bar foo\n\n\nbar foo";
      byte[] byteArray = Encoding.ASCII.GetBytes(value);
      using (var stream = new MemoryStream(byteArray)) {
        using (var reader = new StreamReader(stream)) {
          using (var result = new FindStrStream(8).Search(reader, "bar foo".ToCharArray(), _listPool)) {
            Assert.AreEqual(2, result.Item.Count);

            Assert.AreEqual(1, result.Item[0].LineNumber);
            Assert.AreEqual(5, result.Item[0].ColumnNumber);

            Assert.AreEqual(4, result.Item[1].LineNumber);
            Assert.AreEqual(1, result.Item[1].ColumnNumber);
          }
        }
      }
    }

    [TestMethod]
    public void SearchStreamShouldFindEntriesAcrossBufferBoundaries2() {
      var value = "foo bar foo\n\n\nbar foo";
      byte[] byteArray = Encoding.ASCII.GetBytes(value);
      using (var stream = new MemoryStream(byteArray)) {
        using (var reader = new StreamReader(stream)) {
          using (var result = new FindStrStream(3).Search(reader, "foo".ToCharArray(), _listPool)) {
            Assert.AreEqual(3, result.Item.Count);

            Assert.AreEqual(1, result.Item[0].LineNumber);
            Assert.AreEqual(1, result.Item[0].ColumnNumber);

            Assert.AreEqual(1, result.Item[1].LineNumber);
            Assert.AreEqual(9, result.Item[1].ColumnNumber);

            Assert.AreEqual(4, result.Item[2].LineNumber);
            Assert.AreEqual(5, result.Item[2].ColumnNumber);
          }
        }
      }
    }

    [TestMethod]
    public void SearchStreamShouldFindEntriesAcrossBufferBoundaries3() {
      var value = "foo bar foo\n\n\nbar foo";
      byte[] byteArray = Encoding.ASCII.GetBytes(value);
      using (var stream = new MemoryStream(byteArray)) {
        using (var reader = new StreamReader(stream)) {
          using (var result = new FindStrStream(2).Search(reader, "foo".ToCharArray(), _listPool)) {
            Assert.AreEqual(3, result.Item.Count);

            Assert.AreEqual(1, result.Item[0].LineNumber);
            Assert.AreEqual(1, result.Item[0].ColumnNumber);

            Assert.AreEqual(1, result.Item[1].LineNumber);
            Assert.AreEqual(9, result.Item[1].ColumnNumber);

            Assert.AreEqual(4, result.Item[2].LineNumber);
            Assert.AreEqual(5, result.Item[2].ColumnNumber);
          }
        }
      }
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public void SearchStreamShouldThrowExceptionIfPatternTooBig() {
      var value = "foo bar foo\n\n\nbar foo";
      byte[] byteArray = Encoding.ASCII.GetBytes(value);
      using (var stream = new MemoryStream(byteArray)) {
        using (var reader = new StreamReader(stream)) {
          using (var result = new FindStrStream(2).Search(reader, "foo bar".ToCharArray(), _listPool)) {
          }
        }
      }
    }
  }
}
