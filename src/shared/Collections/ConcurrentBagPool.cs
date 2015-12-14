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

using System;
using System.Collections.Concurrent;

namespace mtsuite.shared.Collections {
  /// <summary>
  /// A thread safe implementation of <see cref="IPool{T}"/> using a <see
  /// cref="ConcurrentBag{T}"/>. Experiments have shown that this particular
  /// implementation is about 2x slower than <see
  /// cref="ConcurrentFixedSizeArrayPool{T}"/>.
  /// </summary>
  public class ConcurrentBagPool<T> : IPool<T> where T : class {
    private readonly Func<T> _creator;
    private readonly Action<T> _recycler;
    private readonly ConcurrentBag<T> _entries;
    private readonly int _size;

    public ConcurrentBagPool(Func<T> creator, Action<T> recycler)
      : this(creator, recycler, Environment.ProcessorCount * 2) {
    }

    public ConcurrentBagPool(Func<T> creator, Action<T> recycler, int size) {
      if (creator == null)
        throw new ArgumentNullException("creator");
      if (recycler == null)
        throw new ArgumentNullException("recycler");
      if (size < 1)
        throw new ArgumentException("Size must be >= 1", "size");
      _creator = creator;
      _recycler = recycler;
      _size = size;
      _entries = new ConcurrentBag<T>();
    }

    public T Allocate() {
      T item;
      if (_entries.TryTake(out item))
        return item;
      return _creator();
    }

    public void Recycle(T item) {
      _recycler(item);
      if (_entries.Count < _size)
        _entries.Add(item);
    }
  }
}