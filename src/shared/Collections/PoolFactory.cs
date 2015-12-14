﻿// Copyright 2015 Renaud Paquay All Rights Reserved.
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

namespace mtsuite.shared.Collections {
  /// <summary>
  /// Default pool factory, create most generally useful <see cref="IPool{T}"/>
  /// instances.
  /// </summary>
  public static class PoolFactory<T> where T : class {
    public static IPool<T> Create(Func<T> creator, Action<T> recycler) {
      return new ConcurrentFixedSizeArrayPool<T>(creator, recycler);
    }

    public static IPool<T> Create(Func<T> creator, Action<T> recycler, int size) {
      return new ConcurrentFixedSizeArrayPool<T>(creator, recycler, size);
    }
  }
}