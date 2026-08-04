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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtsuite.CoreFileSystem.ObjectPool;

namespace tests;

[TestClass]
public class NameTableTest
{
    [TestMethod]
    public void GetOrAdd_BasicLookups_ReturnsSameReference()
    {
        var table = new NameTable();

        string str1 = table.GetOrAdd("hello".AsSpan());
        string str2 = table.GetOrAdd("hello".AsSpan());
        string str3 = table.GetOrAdd("world".AsSpan());

        Assert.AreEqual("hello", str1);
        Assert.AreEqual("world", str3);
        Assert.AreSame(str1, str2); // Exact reference equality
        Assert.AreNotSame(str1, str3);
    }

    [TestMethod]
    public void GetOrAdd_EmptySpan_ReturnsStringEmpty()
    {
        var table = new NameTable();
        string result = table.GetOrAdd(ReadOnlySpan<char>.Empty);
        Assert.AreSame(string.Empty, result);
    }

    [TestMethod]
    public void GetOrAdd_DirectMapped_CollisionHandling()
    {
        // Table with capacity 16 per thread
        var table = new NameTable(capacityPerThread: 16);
        Assert.AreEqual(16, table.Capacity);

        // Add items and verify lookups
        string a1 = table.GetOrAdd("alpha".AsSpan());
        string a2 = table.GetOrAdd("alpha".AsSpan());
        Assert.AreSame(a1, a2);

        // Populate multiple items
        for (int i = 0; i < 20; i++)
        {
            table.GetOrAdd($"item_{i}".AsSpan());
        }

        Assert.IsTrue(table.UniqueStringCount <= 16);
        Assert.AreEqual(22, table.CallCount);
    }

    [TestMethod]
    public void GetOrAdd_ConcurrentAccess_ThreadSafe()
    {
        var table = new NameTable(capacityPerThread: 256);
        const int threadCount = 16;
        const int itemsPerThread = 1000;
        var sampleWords = Enumerable.Range(0, 100).Select(i => $"filename_{i}.txt").ToArray();

        var results = new string[threadCount, itemsPerThread];

        Parallel.For(0, threadCount, threadIndex =>
        {
            for (int i = 0; i < itemsPerThread; i++)
            {
                var word = sampleWords[i % sampleWords.Length];
                results[threadIndex, i] = table.GetOrAdd(word.AsSpan());
            }
        });

        // Verify that all returned strings match the expected words
        for (int t = 0; t < threadCount; t++)
        {
            for (int i = 0; i < itemsPerThread; i++)
            {
                var expected = sampleWords[i % sampleWords.Length];
                Assert.AreEqual(expected, results[t, i]);
            }
        }

        Assert.AreEqual(threadCount * itemsPerThread, table.CallCount);
        Assert.IsTrue(table.UniqueStringCount > 0);
    }

    [TestMethod]
    public void NoCacheNameTable_Statistics_TracksCallCountAndHeapBytes()
    {
        INameTable table = new NoCacheNameTable();
        Assert.AreEqual(0, table.CallCount);
        Assert.IsNull(table.UniqueStringCount);
        Assert.AreEqual(0, table.ApproximateHeapBytes);

        table.GetOrAdd("first".AsSpan());
        table.GetOrAdd("first".AsSpan());
        table.GetOrAdd("second".AsSpan());

        Assert.AreEqual(3, table.CallCount);
        Assert.IsNull(table.UniqueStringCount);
        // "first" (5 chars -> 34 bytes) * 2 + "second" (6 chars -> 36 bytes) = 104 bytes
        Assert.AreEqual(104, table.ApproximateHeapBytes);
    }

    [TestMethod]
    public void NameTable_Statistics_TracksCallCountUniqueStringsAndHeapBytes()
    {
        INameTable table = new NameTable(capacityPerThread: 16);
        Assert.AreEqual(0, table.CallCount);
        Assert.AreEqual(0, table.UniqueStringCount);
        Assert.AreEqual(0, table.ApproximateHeapBytes);

        table.GetOrAdd("alpha".AsSpan());
        long firstBytes = table.ApproximateHeapBytes;
        Assert.IsTrue(firstBytes > 0);

        table.GetOrAdd("alpha".AsSpan()); // hit
        table.GetOrAdd("beta".AsSpan());
        table.GetOrAdd("gamma".AsSpan());

        Assert.AreEqual(4, table.CallCount);
        Assert.AreEqual(3, table.UniqueStringCount);
        Assert.IsTrue(table.ApproximateHeapBytes > firstBytes);
    }
}
