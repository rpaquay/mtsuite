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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace mtsuite.shared {
  public class ThreadProgressTracker {
    private readonly ConcurrentBag<ThreadProgressState> _threadStates = new();
    private readonly ThreadLocal<ThreadProgressState> _currentThreadState;
    private int _threadCounter = 0;

    public ThreadProgressTracker() {
      _currentThreadState = new ThreadLocal<ThreadProgressState>(() => {
        var index = Interlocked.Increment(ref _threadCounter);
        var state = new ThreadProgressState(index, Environment.CurrentManagedThreadId);
        _threadStates.Add(state);
        return state;
      });
    }

    public ThreadProgressState Current => _currentThreadState.Value!;

    public CoreFileSystem.FullPath? SourcePath { get; set; }
    public CoreFileSystem.FullPath? DestinationPath { get; set; }

    public IReadOnlyList<ThreadProgressState> GetAllStates() {
      var list = _threadStates.ToList();
      list.Sort((a, b) => a.ThreadIndex.CompareTo(b.ThreadIndex));
      return list;
    }

    public IReadOnlyList<string> GetFormattedLines() {
      var states = GetAllStates();
      if (states.Count == 0) {
        return Array.Empty<string>();
      }

      var lines = new List<string>(states.Count);
      foreach (var state in states) {
        lines.Add(state.CreateSnapshot().Format(SourcePath, DestinationPath));
      }
      return lines;
    }
  }
}
