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
using System.Collections.Generic;
using System.Threading.Tasks;
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtsuite.shared;

public static class TaskExt {
    public static Task<T[]> WhenAllAndDispose<T>(this FromPool<List<Task<T>>> list) {
        var result = Task.WhenAll(list.Item); 
        list.Dispose();
        return result;
    } 
  
    public static Task WhenAllAndDispose(this FromPool<List<Task>> list) {
        var result = Task.WhenAll(list.Item); 
        list.Dispose();
        return result;
    } 
}