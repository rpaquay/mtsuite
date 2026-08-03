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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using mtsuite.CoreFileSystem.Utils;

namespace tests;

[TestClass]
public class StringSliceTest
{
    [TestMethod]
    public void StringSlice_BasicPropertiesAndIndexing()
    {
        char[] buffer = "HelloWorld".ToCharArray();
        var slice = new StringSlice(buffer, 5, 5);

        Assert.AreEqual(5, slice.Offset);
        Assert.AreEqual(5, slice.Length);
        Assert.IsFalse(slice.IsEmpty);
        Assert.AreEqual('W', slice[0]);
        Assert.AreEqual('d', slice[4]);
        Assert.IsTrue(slice.Span.SequenceEqual("World".AsSpan()));
        Assert.AreEqual("World", slice.ToString());
    }

    [TestMethod]
    public void StringSlice_EmptyBehavior()
    {
        var empty = StringSlice.Empty;
        Assert.IsTrue(empty.IsEmpty);
        Assert.AreEqual(0, empty.Length);
        Assert.AreEqual(string.Empty, empty.ToString());
        Assert.IsTrue(empty.Span.IsEmpty);
    }

    [TestMethod]
    public void StringSlice_EqualityAndHashCode()
    {
        char[] buf1 = "abcdef".ToCharArray();
        char[] buf2 = "123cde456".ToCharArray();

        var slice1 = new StringSlice(buf1, 2, 3); // "cde"
        var slice2 = new StringSlice(buf2, 3, 3); // "cde"
        var slice3 = new StringSlice(buf1, 0, 3); // "abc"

        Assert.IsTrue(slice1.Equals(slice2));
        Assert.IsTrue(slice1 == slice2);
        Assert.IsFalse(slice1 != slice2);
        Assert.AreEqual(slice1.GetHashCode(), slice2.GetHashCode());

        Assert.IsFalse(slice1.Equals(slice3));
        Assert.IsTrue(slice1 != slice3);

        Assert.IsTrue(slice1.Equals("cde"));
        Assert.IsTrue(slice1.Equals("CDE".AsSpan(), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(slice1.Equals("CDE".AsSpan(), StringComparison.Ordinal));
    }

    [TestMethod]
    public void StringSliceFactory_PacksSlicesIntoSameBuffer()
    {
        var factory = new StringSliceFactory(chunkSizeInChars: 1024);

        var slice1 = factory.Create("foo".AsSpan());
        var slice2 = factory.Create("bar".AsSpan());
        var slice3 = factory.Create("baz");

        Assert.AreEqual("foo", slice1.ToString());
        Assert.AreEqual("bar", slice2.ToString());
        Assert.AreEqual("baz", slice3.ToString());

        // All 3 slices should share the exact same buffer instance
        Assert.AreSame(slice1.Buffer, slice2.Buffer);
        Assert.AreSame(slice2.Buffer, slice3.Buffer);

        Assert.AreEqual(0, slice1.Offset);
        Assert.AreEqual(3, slice1.Length);

        Assert.AreEqual(3, slice2.Offset);
        Assert.AreEqual(3, slice2.Length);

        Assert.AreEqual(6, slice3.Offset);
        Assert.AreEqual(3, slice3.Length);
    }

    [TestMethod]
    public void StringSliceFactory_AllocatesNewChunkOnOverflow()
    {
        // Small chunk size of 10 chars
        var factory = new StringSliceFactory(chunkSizeInChars: 10);

        var slice1 = factory.Create("123456"); // 6 chars (offset 0..6)
        var slice2 = factory.Create("789012"); // 6 chars (overflows 10 chars -> allocated in chunk 2)

        Assert.AreEqual("123456", slice1.ToString());
        Assert.AreEqual("789012", slice2.ToString());

        Assert.AreNotSame(slice1.Buffer, slice2.Buffer);
        Assert.AreEqual(2, factory.ChunkCount);
    }

    [TestMethod]
    public void StringSliceFactory_HandlesLargerThanChunkStrings()
    {
        var factory = new StringSliceFactory(chunkSizeInChars: 10);
        string largeText = new string('x', 50);

        var slice = factory.Create(largeText);

        Assert.AreEqual(50, slice.Length);
        Assert.AreEqual(largeText, slice.ToString());
    }

    [TestMethod]
    public void StringSliceFactory_ResetReusesFirstChunk()
    {
        var factory = new StringSliceFactory(chunkSizeInChars: 10);

        var slice1 = factory.Create("hello");
        factory.Reset();
        var slice2 = factory.Create("world");

        Assert.AreEqual(0, slice2.Offset);
        Assert.AreEqual("world", slice2.ToString());
        Assert.AreSame(slice1.Buffer, slice2.Buffer);
    }

    [TestMethod]
    public void StringSliceFactory_ConcurrentCreationsAreThreadSafe()
    {
        var factory = new StringSliceFactory(chunkSizeInChars: 1024);
        const int threadCount = 16;
        const int itemsPerThread = 500;
        var words = Enumerable.Range(0, threadCount * itemsPerThread).Select(i => $"word_{i}").ToArray();
        var slices = new StringSlice[words.Length];

        Parallel.For(0, words.Length, i =>
        {
            slices[i] = factory.Create(words[i].AsSpan());
        });

        // Verify all slices have exact data without race condition corruptions
        for (int i = 0; i < words.Length; i++)
        {
            Assert.AreEqual(words[i], slices[i].ToString());
        }
    }
}
