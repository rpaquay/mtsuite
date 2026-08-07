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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.shared.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests {
  [TestClass]
  public class PoolTest {
    private class Entry {
      public static int GlobalId;
      /// <summary>
      /// Big element to create GC pressure
      /// </summary>
      //public byte[] _elements = new byte[1024 * 1];

      public Entry() {
        Interlocked.Increment(ref GlobalId);
      }
    }

    [TestMethod]
    public void FixedSizeArrayPoolShouldNotAllocateTooMuch() {
      var pool = new ConcurrentFixedSizeArrayPool<Entry>(() => new Entry(), _ => { }, Environment.ProcessorCount);
      RunPoolTest(pool, _ => Entry.GlobalId <= Environment.ProcessorCount);
    }

    private void RunPoolTest<T>(IPool<T> pool, Func<long, bool> verify) where T : class {
      Enumerable.Range(0, 3).ToList().ForEach(_ => {
        Entry.GlobalId = 0;
        var threadCount = Environment.ProcessorCount;
        var waitHandles = new WaitHandle[threadCount];
        const int allocCount = 500;
        var threads = Enumerable.Range(0, threadCount).Select(x => {
          var waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
          waitHandles[x] = waitHandle;
          return new Thread(() => {
            for (var i = 0; i < allocCount; i++) {
              var item = pool.Allocate();
              pool.Recycle(item);
            }
            waitHandle.Set();
          });
        }).ToList();
        var sw = Stopwatch.StartNew();
        foreach (var x in threads) {
          x.Start();
        }
        foreach (var x in threads) {
          x.Join();
        }
        Console.WriteLine("Pool allocated {0:n0} objects over {1:n0} allocation calls from {2:n0} threads in {3} msec",
          Entry.GlobalId, allocCount * threadCount, threadCount, sw.Elapsed.TotalMilliseconds);
        Console.WriteLine();
        Assert.IsTrue(verify(allocCount * threadCount));
      });
    }

    [TestMethod]
    public void MtPoolFactoryTracksPoolStatisticsCorrectly() {
      var poolFactory = new MtPoolFactory();
      var poolName = "TestTrackStatsPool_" + Guid.NewGuid().ToString("N");
      var pool = poolFactory.Create<Entry>(poolName, () => new Entry(), _ => { });

      // IPool<T> directly inherits from INamedPool
      Assert.IsTrue(pool is INamedPool);
      Assert.AreEqual(poolName, pool.Name);
      Assert.AreEqual(typeof(Entry), pool.ItemType);
      Assert.AreEqual(0, pool.RentCount);
      Assert.AreEqual(0, pool.ReturnCount);
      Assert.AreEqual(0, pool.CreatedCount);

      // Allocate first item (creation)
      var item1 = pool.Allocate();
      Assert.AreEqual(1, pool.RentCount);
      Assert.AreEqual(1, pool.CreatedCount);
      Assert.AreEqual(0, pool.ReturnCount);
      Assert.AreEqual(1, pool.OutstandingCount);
      Assert.AreEqual(0, pool.HitCount);

      // Allocate second item (creation)
      var item2 = pool.Allocate();
      Assert.AreEqual(2, pool.RentCount);
      Assert.AreEqual(2, pool.CreatedCount);
      Assert.AreEqual(0, pool.ReturnCount);
      Assert.AreEqual(2, pool.OutstandingCount);

      // Recycle both items
      pool.Recycle(item1);
      pool.Recycle(item2);
      Assert.AreEqual(2, pool.ReturnCount);
      Assert.AreEqual(0, pool.OutstandingCount);

      // Re-allocate (hits from pool)
      var reused1 = pool.Allocate();
      var reused2 = pool.Allocate();
      Assert.AreEqual(4, pool.RentCount);
      Assert.AreEqual(2, pool.CreatedCount); // No new creation!
      Assert.AreEqual(2, pool.HitCount);
      Assert.AreEqual(50.0, pool.HitRatio, 0.01);
      Assert.AreEqual(2, pool.OutstandingCount);

      pool.Recycle(reused1);
      pool.Recycle(reused2);
      Assert.AreEqual(4, pool.ReturnCount);
      Assert.AreEqual(0, pool.OutstandingCount);

      // Test reset
      pool.Reset();
      Assert.AreEqual(0, pool.RentCount);
      Assert.AreEqual(0, pool.ReturnCount);
      Assert.AreEqual(0, pool.CreatedCount);
    }

    [TestMethod]
    public void MtPoolFactoryCreateListCreatesWorkingNamedPool() {
      var poolFactory = new MtPoolFactory();
      var listPoolName = "TestListPool_" + Guid.NewGuid().ToString("N");
      var listPool = poolFactory.CreateList<string>(listPoolName, 128);

      Assert.IsTrue(listPool is INamedPool);
      Assert.AreEqual(listPoolName, listPool.Name);

      using (var rented = listPool.AllocateFrom()) {
        rented.Item.Add("hello");
        rented.Item.Add("world");
        Assert.AreEqual(2, rented.Item.Count);
        Assert.AreEqual(1, listPool.RentCount);
        Assert.AreEqual(1, listPool.OutstandingCount);
      }

      Assert.AreEqual(1, listPool.ReturnCount);
      Assert.AreEqual(0, listPool.OutstandingCount);

      using (var rented2 = listPool.AllocateFrom()) {
        Assert.AreEqual(0, rented2.Item.Count); // Recycled and cleared
        Assert.AreEqual(2, listPool.RentCount);
        Assert.AreEqual(1, listPool.HitCount);
      }
    }
  }
}