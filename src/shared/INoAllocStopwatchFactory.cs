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

namespace mtsuite.shared;

public interface INoAllocStopwatchFactory {
    NoAllocStopwatch Create();
    
    TimeSpan GetElapsed(long startingTimestamp);
}

public class NoAllocStopwatchFactory : INoAllocStopwatchFactory {
    public static readonly NoAllocStopwatchFactory Instance = new NoAllocStopwatchFactory();

    public TimeSpan GetElapsed(long startingTimestamp) {
        return Stopwatch.GetElapsedTime(startingTimestamp);
    }

    public NoAllocStopwatch Create() {
        return new NoAllocStopwatch(this, Stopwatch.GetTimestamp());
    }
}

public readonly struct NoAllocStopwatch(INoAllocStopwatchFactory factory, long startingTimestamp) {
    public TimeSpan Elapsed => factory.GetElapsed(startingTimestamp);
}

