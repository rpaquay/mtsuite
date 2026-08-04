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
    public void GetOrAdd_AutoGrowsAt80PercentLoadFactor()
    {
        // Start with small capacity = 16 (threshold = 12 items)
        var table = new NameTable(initialCapacity: 16);
        Assert.AreEqual(16, table.Capacity);

        // Add 12 items (75% load factor)
        for (int i = 0; i < 12; i++)
        {
            table.GetOrAdd($"item_{i}".AsSpan());
        }
        Assert.AreEqual(16, table.Capacity);

        // Add 13th item (exceeds 80% threshold -> grows to 32)
        table.GetOrAdd("item_12".AsSpan());
        Assert.AreEqual(32, table.Capacity);
        Assert.AreEqual(13, table.Count);

        // Verify all 13 items are still present and return same references
        for (int i = 0; i < 13; i++)
        {
            string expected = $"item_{i}";
            string actual = table.GetOrAdd(expected.AsSpan());
            Assert.AreEqual(expected, actual);
        }
    }

    [TestMethod]
    public void GetOrAdd_ConcurrentAccess_WithDynamicResizing()
    {
        // Start small (capacity = 16) and add thousands of unique items to force multiple concurrent resizes
        var table = new NameTable(initialCapacity: 16);
        const int threadCount = 16;
        const int itemsPerThread = 500;
        var uniqueWords = Enumerable.Range(0, threadCount * itemsPerThread).Select(i => $"unique_word_{i}").ToArray();

        var results = new string[uniqueWords.Length];

        Parallel.For(0, uniqueWords.Length, i =>
        {
            results[i] = table.GetOrAdd(uniqueWords[i].AsSpan());
        });

        // Verify all items are interned correctly and capacity expanded
        Assert.IsTrue(table.Capacity > 16);
        Assert.AreEqual(uniqueWords.Length, table.Count);

        for (int i = 0; i < uniqueWords.Length; i++)
        {
            string internedAgain = table.GetOrAdd(uniqueWords[i].AsSpan());
            Assert.AreSame(results[i], internedAgain);
        }
    }

    [TestMethod]
    public void GetOrAdd_ConcurrentAccess_ThreadSafeAndUniqueReferences()
    {
        var table = new NameTable(initialCapacity: 256);
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

        // Verify that all threads received the exact same reference for each word
        for (int w = 0; w < sampleWords.Length; w++)
        {
            string expectedWord = sampleWords[w];
            string? firstRef = null;

            for (int t = 0; t < threadCount; t++)
            {
                for (int i = w; i < itemsPerThread; i += sampleWords.Length)
                {
                    string actual = results[t, i];
                    Assert.AreEqual(expectedWord, actual);
                    if (firstRef == null)
                    {
                        firstRef = actual;
                    }
                    else
                    {
                        Assert.AreSame(firstRef, actual);
                    }
                }
            }
        }
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
        INameTable table = new NameTable(initialCapacity: 16);
        Assert.AreEqual(0, table.CallCount);
        Assert.AreEqual(0, table.UniqueStringCount);
        long initialBytes = table.ApproximateHeapBytes;
        Assert.IsTrue(initialBytes > 0);

        table.GetOrAdd("alpha".AsSpan());
        table.GetOrAdd("alpha".AsSpan()); // hit
        table.GetOrAdd("beta".AsSpan());
        table.GetOrAdd("gamma".AsSpan());

        Assert.AreEqual(4, table.CallCount);
        Assert.AreEqual(3, table.UniqueStringCount);
        Assert.IsTrue(table.ApproximateHeapBytes > initialBytes);
    }
}
