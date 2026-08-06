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
#nullable enable

using System;
using System.Diagnostics;
using System.Threading;

namespace mtsuite.CoreFileSystem.ObjectPool;

/// <summary>
/// A high-performance, thread-safe implementation of <see cref="IPool{T}"/> using a
/// per-thread <see cref="ThreadState"/> cache and a thread-distributed array for zero-contention pooling.
/// </summary>
public class ConcurrentFixedSizeArrayPool<T> : IPool<T> where T : class {
  [DebuggerDisplay("{Value}")]
  private struct Entry {
    public T? Value;
  }

  /// <summary>
  /// Per-thread state holding a small local stack of pooled instances for zero-contention access.
  /// </summary>
  private sealed class ThreadState {
    public readonly T?[] Items;
    public int Count;

    public ThreadState(int capacity) {
      Items = new T?[capacity];
      Count = 0;
    }
  }

  /// <summary>
  /// Instance creation function, used when pool is empty.
  /// </summary>
  private readonly Func<T> _creator;

  /// <summary>
  /// Instance recycle function: used everytime an object is put back into the
  /// pool.
  /// </summary>
  private readonly Action<T> _recycler;

  /// <summary>
  /// Per-thread cache state providing instance-isolated, multi-depth thread-local pooling.
  /// </summary>
  private readonly ThreadLocal<ThreadState> _threadState;

  /// <summary>
  /// Shared slots used to store recycled instances across threads.
  /// </summary>
  private readonly Entry[] _entries;

  /// <summary>
  /// Bitmask for power-of-two array indexing.
  /// </summary>
  private readonly int _mask;

  /// <summary>
  /// Maximum number of items cached locally per thread.
  /// </summary>
  private readonly int _localCapacity;

  public ConcurrentFixedSizeArrayPool(Func<T> creator, Action<T> recycler)
    : this(creator, recycler, Environment.ProcessorCount * 2, localCapacityPerThread: 4) {
  }

  public ConcurrentFixedSizeArrayPool(Func<T> creator, Action<T> recycler, int size)
    : this(creator, recycler, size, localCapacityPerThread: 4) {
  }

  public ConcurrentFixedSizeArrayPool(Func<T> creator, Action<T> recycler, int size, int localCapacityPerThread) {
    _creator = creator ?? throw new ArgumentNullException(nameof(creator));
    _recycler = recycler ?? throw new ArgumentNullException(nameof(recycler));
    if (size < 1)
      throw new ArgumentException("Size must be >= 1", nameof(size));

    _localCapacity = Math.Max(1, localCapacityPerThread);
    _threadState = new ThreadLocal<ThreadState>(() => new ThreadState(_localCapacity));

    // Round up size to next power of 2 for fast bitwise indexing
    int capacity = 1;
    while (capacity < size && capacity < (1 << 16)) capacity <<= 1;

    _entries = new Entry[capacity];
    _mask = capacity - 1;
  }

  public T Allocate() {
    // Fast path: thread-local stack (0 atomic operations, 0 bus contention, instance-isolated)
    var state = _threadState.Value!;
    if (state.Count > 0) {
      state.Count--;
      var item = state.Items[state.Count]!;
      state.Items[state.Count] = null;
      return item;
    }

    // Shared array fallback: scatter search starting at thread-hashed offset
    int length = _entries.Length;
    int start = (Environment.CurrentManagedThreadId * 11) & _mask;

    for (int i = 0; i < length; i++) {
      int idx = (start + i) & _mask;
      var current = _entries[idx].Value;
      if (current != null) {
        var item = Interlocked.CompareExchange(ref _entries[idx].Value, null, current);
        if (item == current) {
          return item;
        }
      }
    }

    return _creator();
  }

  public void Recycle(T item) {
    if (item == null)
      return;

    _recycler(item);

    // Fast path: recycle into thread-local stack if space is available
    var state = _threadState.Value!;
    if (state.Count < _localCapacity) {
      state.Items[state.Count] = item;
      state.Count++;
      return;
    }

    // Shared array fallback: scatter search starting at thread-hashed offset
    int length = _entries.Length;
    int start = (Environment.CurrentManagedThreadId * 11) & _mask;

    for (int i = 0; i < length; i++) {
      int idx = (start + i) & _mask;
      var current = _entries[idx].Value;
      if (current == null) {
        if (Interlocked.CompareExchange(ref _entries[idx].Value, item, null) == null) {
          return;
        }
      }
    }
  }
}