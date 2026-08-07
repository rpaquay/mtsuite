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

namespace mtsuite.CoreFileSystem.ObjectPool;

/// <summary>
/// Represents a named object pool that exposes operational statistics.
/// </summary>
public interface INamedPool {
  /// <summary>
  /// Human-readable identifier assigned by the caller for reporting and diagnostics.
  /// </summary>
  string Name { get; }

  /// <summary>
  /// Total number of allocations requested from the pool.
  /// </summary>
  long RentCount { get; }

  /// <summary>
  /// Total number of recycled items returned to the pool.
  /// </summary>
  long ReturnCount { get; }

  /// <summary>
  /// Total number of new object instances created when no recycled instance was available.
  /// </summary>
  long CreatedCount { get; }

  /// <summary>
  /// Total number of allocations satisfied from cached/recycled objects (RentCount - CreatedCount).
  /// </summary>
  long HitCount => Math.Max(0, RentCount - CreatedCount);

  /// <summary>
  /// Percentage of allocations satisfied from cached/recycled objects.
  /// </summary>
  double HitRatio => RentCount > 0 ? (double)HitCount / RentCount * 100.0 : 100.0;

  /// <summary>
  /// Current number of outstanding rented objects (RentCount - ReturnCount).
  /// </summary>
  long OutstandingCount => Math.Max(0, RentCount - ReturnCount);
}
