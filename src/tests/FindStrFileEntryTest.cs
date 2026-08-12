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
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtfindstr;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;
using tests.FileSystemHelpers;

namespace tests {
  [TestClass]
  public class FindStrFileEntryTest {
    private FileSystemSetup _fileSystemSetup;
    private MtPoolFactory _poolFactory;

    [TestInitialize]
    public void Setup() {
      _fileSystemSetup = new FileSystemSetup();
      _poolFactory = new MtPoolFactory();
    }

    [TestCleanup]
    public void Cleanup() {
      _fileSystemSetup.Dispose();
      _fileSystemSetup = null;
      _poolFactory = null;
    }

    [TestMethod]
    public void SearchFileShouldFindBasicMatches() {
      var file = _fileSystemSetup.Root.CreateFile("basic.txt", 10);
      File.WriteAllText(file.Path.FullName, "hello world\nsecond hello");

      var searcher = new FindStrFileEntry("hello", _poolFactory);
      using (var result = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file.Path))) {
        Assert.AreEqual(2, result.Item.Count);

        Assert.AreEqual(1, result.Item[0].LineNumber);
        Assert.AreEqual(1, result.Item[0].ColumnNumber);

        Assert.AreEqual(2, result.Item[1].LineNumber);
        Assert.AreEqual(8, result.Item[1].ColumnNumber);
      }
    }

    [TestMethod]
    public void SearchFileShouldReturnEmptyForEmptyFile() {
      var file = _fileSystemSetup.Root.CreateFile("empty.txt", 0);
      File.WriteAllText(file.Path.FullName, "");

      var searcher = new FindStrFileEntry("hello", _poolFactory);
      using (var result = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file.Path))) {
        Assert.AreEqual(0, result.Item.Count);
      }
    }

    [TestMethod]
    public void SearchFileShouldReturnEmptyIfPatternTooLong() {
      var file = _fileSystemSetup.Root.CreateFile("short.txt", 5);
      File.WriteAllText(file.Path.FullName, "abc");

      var searcher = new FindStrFileEntry("longerpattern", _poolFactory);
      using (var result = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file.Path))) {
        Assert.AreEqual(0, result.Item.Count);
      }
    }

    [TestMethod]
    public void SearchFileShouldFindMatchAtBoundary() {
      // BufferSize is 1MB. We want to place pattern "hello" exactly split across 1MB boundary.
      // 1MB is 1024 * 1024 = 1,048,576 bytes.
      // Let's write a file of size 1,048,576 + 100 bytes.
      // The prefix will be 'a's.
      // The pattern "hello" starts at index 1024 * 1024 - 2 (so "he" ends the first block, "llo" starts the next block).
      int boundaryIndex = 1024 * 1024;
      int matchStartIndex = boundaryIndex - 2;

      var sb = new StringBuilder();
      sb.Append(new string('a', matchStartIndex));
      sb.Append("hello");
      sb.Append(new string('a', 100));

      var file = _fileSystemSetup.Root.CreateFile("boundary.txt", boundaryIndex + 105);
      File.WriteAllText(file.Path.FullName, sb.ToString());

      var searcher = new FindStrFileEntry("hello", _poolFactory);
      using (var result = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file.Path))) {
        Assert.AreEqual(1, result.Item.Count);
        Assert.AreEqual(1, result.Item[0].LineNumber);
        // boundaryIndex is 1,048,576. matchStartIndex is 1,048,574.
        // Index is 0-based, so column number is matchStartIndex + 1 = 1,048,575.
        Assert.AreEqual(matchStartIndex + 1, result.Item[0].ColumnNumber);
      }
    }

    [TestMethod]
    public void SearchFileShouldCorrectlyCalculateUnicodeUTF8Columns() {
      // "Привет, мир! hello" in UTF-8:
      // Привет (12 bytes, 6 chars)
      // ,  (2 bytes, 2 chars)
      // мир! (9 bytes, 5 chars)
      // hello starts at character index 6 + 2 + 5 = 13 (0-based) -> Column 14.
      // But byte offset is 12 + 2 + 9 = 23 (0-based).
      var file = _fileSystemSetup.Root.CreateFile("unicode.txt", 100);
      File.WriteAllText(file.Path.FullName, "Привет, мир! hello\n😊 hello");

      var searcher = new FindStrFileEntry("hello", _poolFactory);
      using (var result = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file.Path))) {
        Assert.AreEqual(2, result.Item.Count);

        // Line 1: Column should be 14 (Привет=6, comma=1, space=1, мир!=4, space=1, total preceding chars = 13)
        Assert.AreEqual(1, result.Item[0].LineNumber);
        Assert.AreEqual(14, result.Item[0].ColumnNumber);

        // Line 2: 😊 hello. Emojis 😊 are 4 bytes but count as 1 character. Space is 1 character.
        // Total preceding characters = 2. Column should be 3.
        Assert.AreEqual(2, result.Item[1].LineNumber);
        Assert.AreEqual(3, result.Item[1].ColumnNumber);
      }
    }

    [TestMethod]
    public void SearchFileShouldSkipBinaryFiles() {
      // Write binary contents containing the pattern
      var data = new byte[1000];
      // Inject some null bytes to make it binary
      for (int i = 0; i < 500; i++) {
        data[i] = 0;
      }
      var patternBytes = Encoding.UTF8.GetBytes("hello");
      Array.Copy(patternBytes, 0, data, 600, patternBytes.Length);

      var file = _fileSystemSetup.Root.CreateFile("binary.bin", 1000);
      File.WriteAllBytes(file.Path.FullName, data);

      var searcher = new FindStrFileEntry("hello", _poolFactory);
      using (var result = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file.Path))) {
        // Should skip binary files
        Assert.AreEqual(0, result.Item.Count);
      }

      // Regression check: verify fallback was not triggered by asserting we only rented from the list pool once
      var listPool = _poolFactory.RegisteredPools.FirstOrDefault(p => p.Name == "FindStrEntries");
      if (listPool != null) {
        Assert.AreEqual(1, listPool.RentCount);
      }
    }

    [TestMethod]
    public void SearchFileShouldSkipBinaryFilesWithoutNullBytesWithoutFallback() {
      // Write random non-ASCII, non-control bytes (all in [128, 255] range but violating UTF-8 validation and having low ASCII ratio)
      // Since they are all >= 128, there are no null bytes.
      // But they violate UTF-8, so IsValidUtf8 returns false.
      // And since they are all >= 128, the ASCII ratio check (which counts only printable ASCII) sees 0% ASCII.
      // So it should be skipped as binary immediately, without fallback!
      var data = new byte[100];
      for (int i = 0; i < 100; i++) {
        data[i] = 0x80; // loose continuation byte (invalid UTF-8, and non-ASCII)
      }
      
      var file = _fileSystemSetup.Root.CreateFile("binary_no_null.bin", 100);
      File.WriteAllBytes(file.Path.FullName, data);

      var searcher = new FindStrFileEntry("hello", _poolFactory);
      using (var result = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file.Path))) {
        Assert.AreEqual(0, result.Item.Count);
      }

      var listPool = _poolFactory.RegisteredPools.FirstOrDefault(p => p.Name == "FindStrEntries");
      if (listPool != null) {
        Assert.AreEqual(1, listPool.RentCount); // Verify fallback was not triggered
      }
    }

    [TestMethod]
    public void SearchFileShouldCorrectlyPoolAndRecycleBuffersAndLists() {
      var file1 = _fileSystemSetup.Root.CreateFile("pool1.txt", 20);
      File.WriteAllText(file1.Path.FullName, "pattern match here\npattern again");

      var file2 = _fileSystemSetup.Root.CreateFile("pool2.txt", 20);
      File.WriteAllText(file2.Path.FullName, "pattern matches\nno matches");

      var searcher = new FindStrFileEntry("pattern", _poolFactory);

      var listPool = _poolFactory.RegisteredPools.FirstOrDefault(p => p.Name == "FindStrEntries");
      var bytePool = _fileSystemSetup.FileSystem is FileSystemPortable
        ? null // FileSystemPortable doesn't use FileIOByteArrayPool, it uses its own or delegates
        : _poolFactory.RegisteredPools.FirstOrDefault(p => p.Name == "FileIOByteArrayPool");

      // Verify no outstanding rentals before starting
      if (listPool != null) {
        Assert.AreEqual(0, listPool.OutstandingCount);
      }
      if (bytePool != null) {
        Assert.AreEqual(0, bytePool.OutstandingCount);
      }

      using (var res1 = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file1.Path))) {
        using (var res2 = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file2.Path))) {
          Assert.AreEqual(2, res1.Item.Count);
          Assert.AreEqual(1, res2.Item.Count);

          // There should be outstanding rentals since we haven't disposed the results yet
          if (listPool != null) {
            Assert.IsTrue(listPool.OutstandingCount >= 2);
          }
        }
      }

      // After disposal, outstanding count should go back to 0
      if (listPool != null) {
        Assert.AreEqual(0, listPool.OutstandingCount);
      }
      if (bytePool != null) {
        Assert.AreEqual(0, bytePool.OutstandingCount);
      }
    }

    [TestMethod]
    public void SearchFileShouldFallbackForNonUtf8Files() {
      var file = _fileSystemSetup.Root.CreateFile("ansi.txt", 100);

      // Write "é hello" followed by longer text in Latin1 encoding. é is 0xE9, which is invalid UTF-8.
      using (var stream = File.OpenWrite(file.Path.FullName)) {
        using (var writer = new StreamWriter(stream, Encoding.Latin1)) {
          writer.Write("é hello. This is a longer text to make sure the file is not classified as binary by the ratio check.");
        }
      }

      var searcher = new FindStrFileEntry("hello", _poolFactory);
      using (var result = searcher.SearchFile(_fileSystemSetup.FileSystem, _fileSystemSetup.FileSystem.GetEntry(file.Path))) {
        Assert.AreEqual(1, result.Item.Count);
        Assert.AreEqual(1, result.Item[0].LineNumber);
        // Column should be 3 (é is 1 char, space is 1 char)
        Assert.AreEqual(3, result.Item[0].ColumnNumber);
      }
    }
  }
}
