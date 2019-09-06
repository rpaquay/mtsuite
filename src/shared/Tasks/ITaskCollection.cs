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
using System.Collections.Generic;

namespace mtsuite.shared.Tasks {
  public interface ITaskCollection : IEnumerable<ITask> {
    int Count { get; }

    void Add(ITask task);
    void AddRange(IEnumerable<ITask> tasks);

    /// <summary>
    /// Returns a <see cref="ITask"/> that completes when all tasks in the collection and a follow up <see cref="Action"/> are completed.
    /// The parameter is a lamdba that contains the code to run after all tasks in the collection are completed.
    /// </summary>
    ITask ContinueWith(Action<ITaskCollection> continuation);
    /// <summary>
    /// Returns a <see cref="ITask{TResult}"/> that completes when all tasks in the collection and a follow up <see cref="Func{ITaskCollection, TResult}"/> are completed.
    /// The parameter is a lamdba that contains the code to run after all tasks in the collection are completed.
    /// </summary>
    ITask<TResult> ContinueWith<TResult>(Func<ITaskCollection, TResult> continuation);

    /// <summary>
    /// Returns a <see cref="ITask"/> that completes when all tasks in the collection and a follow up <see cref="ITask"/> are completed.
    /// The parameter is a lamdba that creates the follow up task to run after all tasks in the collection are completed.
    /// </summary>
    ITask Then(Func<ITaskCollection, ITask> taskFactory);
    /// <summary>
    /// Returns a <see cref="ITask{TResult}"/> that completes when all tasks in the collection and a follow up <see cref="ITask{TResult}"/> are completed.
    /// The parameter is a lamdba that creates the follow up task to run after all tasks in the collection are completed.
    /// </summary>
    ITask<TResult> Then<TResult>(Func<ITaskCollection, ITask<TResult>> taskFactory);
  }
}