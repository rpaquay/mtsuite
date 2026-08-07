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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using mtsuite.CoreFileSystem;
using mtsuite.CoreFileSystem.ObjectPool;
using mtsuite.CoreFileSystem.Utils;
using mtsuite.shared.Utils;

namespace mtsuite.shared.CommandLine {
  public static class ProgramHelpers {
    public static int RunProgram(string[] args, Action program) {
      try {
        program();
        return 0;
      } catch (CommandLineReturnValueException e) {
        return e.ReturnCode;
      } catch (Exception e) {
        Console.Error.WriteLine("Unexpected error(s):");
        foreach (var error in FlattenErrors(e)) {
          Console.Error.WriteLine("  {0}", error.Message);
          Console.Error.WriteLine("  {0}", error.StackTrace);
        }
        return 255;
      }
    }

    public static FullPath MakeFullPath(string pathValue) {
      return new FullPath(PathHelpers.NormalizeUserInputPath(Environment.CurrentDirectory, pathValue));
    }

    public static void DisplayErrors(IEnumerable<Exception> errors) {
      foreach (var error in FlattenErrors(errors)) {
        if (IsInternalError(error)) {
          Console.Error.WriteLine("Internal error: {0}", error.Message);
          foreach (var line in error.StackTrace.Replace("\r\n", "\n").Split('\n')) {
            Console.Error.WriteLine("    {0}", line);
          }
        } else {
          Console.Error.WriteLine("Error: {0}", error.Message);
        }
      }
    }

    public static void DisplayWarnings(IEnumerable<Exception> warnings) {
      foreach (var error in FlattenErrors(warnings)) {
        Console.Error.WriteLine("Warning: {0}", error.Message);
      }
    }

    private static bool IsInternalError(Exception error) {
      return error is ArgumentException ||
        error is NullReferenceException ||
        error is InvalidOperationException;
    }

    public static IEnumerable<Exception> FlattenErrors(Exception error) {
      var agg = error as AggregateException;
      if (agg != null) {
        foreach (var inner in FlattenErrors(agg.InnerExceptions)) {
          foreach (var x in FlattenErrors(inner)) {
            yield return x;
          }
        }
      } else {
        for (Exception inner = error; inner != null; inner = inner.InnerException) {
          yield return inner;
        }
      }
    }

    public static IEnumerable<Exception> FlattenErrors(IEnumerable<Exception> errors) {
      return errors.SelectMany(error => FlattenErrors(error));
    }

    public static void SetWorkerThreadCount(int count) {
      if (count <= 0)
        return;
      int mint, minc;
      ThreadPool.GetMinThreads(out mint, out minc);
      int maxt, maxc;
      ThreadPool.GetMaxThreads(out maxt, out maxc);
      ThreadPool.SetMinThreads(count, minc);
      ThreadPool.SetMaxThreads(count, maxc);
    }
    // ThreadPool.GetMinThreads(out var mint, out var minc);
    //   ThreadPool.GetMaxThreads(out var maxt, out var maxc);
    //   int newMax = Math.Max(count, maxt);
    //   ThreadPool.SetMaxThreads(newMax, maxc);
    //   ThreadPool.SetMinThreads(count, minc);
    //   ThreadPool.SetMaxThreads(Math.Max(count, Environment.ProcessorCount), maxc);
    // }

    public static void DisplayFullStatistics(Statistics statistics) {
      Console.WriteLine();
      Console.WriteLine("Statistics:");
      Console.WriteLine("  Elapsed time:             {0}", FormatHelpers.FormatElapsedTime(statistics.ElapsedTime));
      Console.WriteLine("  CPU time:                 {0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      Console.WriteLine("  # of source directories:    {0:n0}", statistics.DirectoryTraversedCount);
      Console.WriteLine("  # of source files:          {0:n0}", statistics.FileCopiedCount + statistics.FileSkippedCount);
      Console.WriteLine("  # of source symlinks:       {0:n0}", statistics.SymlinkCopiedCount + statistics.SymlinkSkippedCount);
      Console.WriteLine("  Copied entries");
      Console.WriteLine("    # of directories created: {0:n0}", statistics.DirectoryCreatedCount);
      Console.WriteLine("    # of files copied:        {0:n0}", statistics.FileCopiedCount);
      Console.WriteLine("    # of symlinks copied:     {0:n0}", statistics.SymlinkCopiedCount);
      var fileSizeTotalMb = statistics.FileCopiedTotalSize / 1024 / 1024;
      Console.WriteLine("    Total bytes copied:       {0:n0} MB", fileSizeTotalMb);
      Console.WriteLine("    Throughput:               {0:n2} MB/sec",
        fileSizeTotalMb / statistics.ElapsedTime.TotalSeconds);

      Console.WriteLine("  Deleted entries");
      Console.WriteLine("    # of directories deleted: {0:n0}", statistics.DirectoryDeletedCount);
      Console.WriteLine("    # of files deleted:       {0:n0}", statistics.FileDeletedCount);
      Console.WriteLine("    # of symlinks deleted:    {0:n0}", statistics.SymlinkDeletedCount);

      Console.WriteLine("  Skipped entries");
      var fileSkippedTotalSizeMb = statistics.FileSkippedTotalSize / 1024 / 1024;
      Console.WriteLine("    # of files skipped:       {0:n0}", statistics.FileSkippedCount);
      Console.WriteLine("    # of symlinks skipped:    {0:n0}", statistics.SymlinkSkippedCount);
      Console.WriteLine("    Total bytes skipped:      {0:n0} MB", fileSkippedTotalSizeMb);

      Console.WriteLine("  # entries/sec:            {0:n0}",
        (statistics.EntryCopiedCount + statistics.EntryDeletedCount + statistics.FileSkippedCount) /
        statistics.ElapsedTime.TotalSeconds);

      Console.WriteLine("  # of errors:              {0:n0}", statistics.Errors.Count);
      DisplayErrors(statistics.Errors);
    }

    public static void DisplayCompactStatistics(Statistics statistics, bool isDryRun = false) {
      Console.WriteLine();
      if (isDryRun) {
        Console.WriteLine("Statistics (Simulation Mode):");
      } else {
        Console.WriteLine("Statistics:");
      }
      Console.WriteLine("  Elapsed time:             {0}", FormatHelpers.FormatElapsedTime(statistics.ElapsedTime));
      Console.WriteLine("  CPU time:                 {0}", FormatHelpers.FormatElapsedTime(statistics.TotalProcessorTime));
      Console.WriteLine("  # of directories:         {0:n0}", statistics.DirectoryTraversedCount);
      if (isDryRun) {
        Console.WriteLine("  Identical entries (Potential clones)");
        var fileCompactedMb = statistics.FileCompactedTotalSize / 1024 / 1024;
        Console.WriteLine("    # of files to compact:  {0:n0}", statistics.FileCompactedCount);
        Console.WriteLine("    Potential space savings:{0:n0} MB", fileCompactedMb);
      } else {
        Console.WriteLine("  Compacted entries");
        var fileCompactedMb = statistics.FileCompactedTotalSize / 1024 / 1024;
        Console.WriteLine("    # of files compacted:   {0:n0}", statistics.FileCompactedCount);
        Console.WriteLine("    Total bytes compacted:  {0:n0} MB", fileCompactedMb);
      }
      Console.WriteLine("  Skipped entries");
      var fileSkippedMb = statistics.FileCompactSkippedTotalSize / 1024 / 1024;
      Console.WriteLine("    # of files skipped:     {0:n0}", statistics.FileCompactSkippedCount);
      Console.WriteLine("    Total bytes skipped:    {0:n0} MB", fileSkippedMb);
      Console.WriteLine("  # of errors:              {0:n0}", statistics.Errors.Count);
      DisplayErrors(statistics.Errors);
    }

    public static void DisplayGcStatistics(IFileSystem fileSystem = null) {
      Console.WriteLine();
      var sb = new StringBuilder();
      sb.AppendFormat("GC Memory: {0:n0} KB", GC.GetTotalMemory(false) / 1024);
      for (var i = 0; i <= GC.MaxGeneration; i++) {
        sb.AppendFormat(", Gen{0}: {1:n0}", i, GC.CollectionCount(i));
      }

      var gcInfo = GC.GetGCMemoryInfo();
      sb.AppendFormat(", GC Pause: {0:N2} ms ({1:F2}%)",
        GC.GetTotalPauseDuration().TotalMilliseconds,
        gcInfo.PauseTimePercentage);
      sb.AppendFormat(", Total Allocated: {0:N2} MB",
        GC.GetTotalAllocatedBytes() / (1024.0 * 1024.0));

      Console.WriteLine(sb.ToString());

      DisplayPoolStatistics();
    }

    public static void DisplayPoolStatistics() {
      var pools = MtPoolFactory.Instance.RegisteredPools;
      if (pools.Count == 0)
        return;

      Console.WriteLine();
      Console.WriteLine("Pool Statistics:");
      Console.WriteLine("  {0,-38} {1,10} {2,10} {3,10} {4,8} {5,10}",
        "Pool Name", "Rented", "Recycled", "Created", "Hit %", "In-Use");
      Console.WriteLine("  {0}", new string('-', 92));

      foreach (var pool in pools.OrderBy(p => p.Name)) {
        if (pool.RentCount == 0 && pool.CreatedCount == 0)
          continue;

        Console.WriteLine("  {0,-38} {1,10:n0} {2,10:n0} {3,10:n0} {4,7:F1}% {5,10:n0}",
          pool.Name,
          pool.RentCount,
          pool.ReturnCount,
          pool.CreatedCount,
          pool.HitRatio,
          pool.OutstandingCount);
      }
    }
  }
}