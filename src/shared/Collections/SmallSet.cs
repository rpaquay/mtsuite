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

namespace mtsuite.shared.Collections {
  /// <summary>
  /// Implementation of a set that allows retrieving elements stored in a given
  /// <see cref="IList{T}"/> from a key surrogate element. <see
  /// cref="SmallSet{T}"/> uses a simple reference to the source list for small
  /// collections, or creates a dictionary for larger collections.
  /// </summary>
  public class SmallSet<T> {
    public const int Threshold = 20;
    private readonly IEqualityComparer<T> _comparer;
    private List<T> _itemsList;
    private Dictionary<T, T> _itemsDic;

    public SmallSet()
      : this(EqualityComparer<T>.Default) {
    }

    public SmallSet(IEqualityComparer<T> comparer) {
      _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public SmallSet(List<T> items) : this(items, EqualityComparer<T>.Default) {
    }

    public SmallSet(List<T> items, IEqualityComparer<T> comparer) : this(comparer) {
      SetList(items);
    }

    public void SetList(List<T> items) {
      if (items == null) {
        Clear();
        return;
      }

      if (items.Count > Threshold) {
        if (_itemsDic == null) {
          _itemsDic = new Dictionary<T, T>(items.Count, _comparer);
        } else {
          _itemsDic.Clear();
        }
        foreach (var x in items) {
          _itemsDic.TryAdd(x, x);
        }
        _itemsList = null;
      } else {
        _itemsList = items;
        _itemsDic?.Clear();
      }
    }

    public void Clear() {
      if (_itemsDic != null)
        _itemsDic.Clear();
      _itemsList = null;
    }

    public bool Contains(T item) {
      if (_itemsDic != null && _itemsDic.Count > 0) {
        return _itemsDic.ContainsKey(item);
      }
      if (_itemsList != null) {
        for (var i = 0; i < _itemsList.Count; i++) {
          if (_comparer.Equals(item, _itemsList[i])) {
            return true;
          }
        }
      }
      return false;
    }

    public bool TryGet(T key, out T value) {
      if (_itemsDic != null && _itemsDic.Count > 0) {
        return _itemsDic.TryGetValue(key, out value);
      }
      if (_itemsList != null) {
        for (var i = 0; i < _itemsList.Count; i++) {
          var item = _itemsList[i];
          if (_comparer.Equals(key, item)) {
            value = item;
            return true;
          }
        }
      }
      value = default(T);
      return false;
    }

    public KeyValuePair<bool, T> TryGet(T key) {
      var found = TryGet(key, out var value);
      return new KeyValuePair<bool, T>(found, value);
    }
  }
}