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
using System.Collections.Generic;

namespace mtsuite.CoreFileSystem.ObjectPool;

/// <summary>
/// Consolidated pool factory that creates, names, and tracks all object and array pools across mtsuite.
/// </summary>
public class MtPoolFactory {
  public static MtPoolFactory Instance { get; } = new();

  private readonly List<INamedPool> _pools = new();
  private readonly object _lock = new();

  /// <summary>
  /// Gets a snapshot of all registered named pools.
  /// </summary>
  public IReadOnlyList<INamedPool> RegisteredPools {
    get {
      lock (_lock) {
        return _pools.ToArray();
      }
    }
  }

  /// <summary>
  /// Creates and registers a new named pool with a default recycler (no-op).
  /// </summary>
  public IPool<T> Create<T>(string name, Func<T> creator) where T : class {
    return Create(name, creator, static _ => { });
  }

  /// <summary>
  /// Creates and registers a new named pool with custom creator and recycler.
  /// </summary>
  public IPool<T> Create<T>(string name, Func<T> creator, Action<T> recycler) where T : class {
    return Create(name, creator, recycler, Environment.ProcessorCount * 2, localCapacityPerThread: 4);
  }

  /// <summary>
  /// Creates and registers a new named pool with custom creator, recycler, global capacity, and per-thread cache depth.
  /// </summary>
  public IPool<T> Create<T>(string name, Func<T> creator, Action<T> recycler, int size, int localCapacityPerThread = 4) where T : class {
    ArgumentNullException.ThrowIfNull(name);
    ArgumentNullException.ThrowIfNull(creator);
    ArgumentNullException.ThrowIfNull(recycler);

    lock (_lock) {
      for (int i = 0; i < _pools.Count; i++) {
        if (_pools[i].Name == name && _pools[i] is IPool<T> existing) {
          return existing;
        }
      }

      var pool = new ConcurrentFixedSizeArrayPool<T>(name, creator, recycler, size, localCapacityPerThread);
      _pools.Add(pool);
      return pool;
    }
  }

  /// <summary>
  /// Creates and registers a named pool of <see cref="List{T}"/> instances with automatic clearing on recycle.
  /// </summary>
  public IPool<List<T>> CreateList<T>(string name, int initialCapacity = 256) {
    return Create(name, () => new List<T>(initialCapacity), static list => list.Clear());
  }

  /// <summary>
  /// Resets all statistics across all registered pools.
  /// </summary>
  public void ResetStatistics() {
    lock (_lock) {
      foreach (var pool in _pools) {
        pool.Reset();
      }
    }
  }
}
