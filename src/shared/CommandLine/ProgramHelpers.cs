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

    public static void DisplaySingleError(Exception error) {
      foreach (var err in FlattenErrors(error)) {
        if (IsInternalError(err)) {
          Console.Error.WriteLine("Internal error: {0}", err.Message);
          if (err.StackTrace != null) {
            foreach (var line in err.StackTrace.Replace("\r\n", "\n").Split('\n')) {
              Console.Error.WriteLine("    {0}", line);
            }
          }
        } else {
          Console.Error.WriteLine("Error: {0}", err.Message);
        }
      }
    }

    public static void DisplaySingleWarning(Exception warning) {
      foreach (var err in FlattenErrors(warning)) {
        Console.Error.WriteLine("Warning: {0}", err.Message);
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
      Console.WriteLine("  # of source directories:  {0:n0}", statistics.DirectoryTraversedCount);
      Console.WriteLine("  # of source files:        {0:n0}", statistics.FileEnumeratedCount);
      Console.WriteLine("  # of source links:        {0:n0}", statistics.SymlinkEnumeratedCount);
      Console.WriteLine("  Total source files size:  {0}", FormatHelpers.FormatSize(statistics.FileEnumeratedTotalSize));
      if (statistics.FileClonedCount > 0) {
        Console.WriteLine("  Cloned entries");
        Console.WriteLine("    # of directories created: {0:n0}", statistics.DirectoryCreatedCount);
        Console.WriteLine("    # of files cloned:        {0:n0}", statistics.FileClonedCount);
        Console.WriteLine("    # of links copied:        {0:n0}", statistics.SymlinkCopiedCount);
        Console.WriteLine("    Total bytes cloned:       {0}", FormatHelpers.FormatSize(statistics.FileClonedTotalSize));
        Console.WriteLine("    Throughput:               {0}", FormatHelpers.FormatThroughput(statistics.FileClonedTotalSize, statistics.ElapsedTime.TotalSeconds));
      } else {
        Console.WriteLine("  Copied entries");
        Console.WriteLine("    # of directories created: {0:n0}", statistics.DirectoryCreatedCount);
        Console.WriteLine("    # of files copied:        {0:n0}", statistics.FileCopiedCount);
        Console.WriteLine("    # of links copied:        {0:n0}", statistics.SymlinkCopiedCount);
        Console.WriteLine("    Total bytes copied:       {0}", FormatHelpers.FormatSize(statistics.FileCopiedTotalSize));
        Console.WriteLine("    Throughput:               {0}", FormatHelpers.FormatThroughput(statistics.FileCopiedTotalSize, statistics.ElapsedTime.TotalSeconds));
      }

      Console.WriteLine("  Deleted entries");
      Console.WriteLine("    # of directories deleted: {0:n0}", statistics.DirectoryDeletedCount);
      Console.WriteLine("    # of files deleted:       {0:n0}", statistics.FileDeletedCount);
      Console.WriteLine("    # of links deleted:       {0:n0}", statistics.SymlinkDeletedCount);

      Console.WriteLine("  Skipped entries");
      Console.WriteLine("    # of files skipped:       {0:n0}", statistics.FileSkippedCount);
      Console.WriteLine("    # of links skipped:       {0:n0}", statistics.SymlinkSkippedCount);
      Console.WriteLine("    Total bytes skipped:      {0}", FormatHelpers.FormatSize(statistics.FileSkippedTotalSize));

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
      Console.WriteLine("  # of source directories:  {0:n0}", statistics.DirectoryTraversedCount);
      Console.WriteLine("  # of source files:        {0:n0}", statistics.FileEnumeratedCount);
      Console.WriteLine("  # of source links:        {0:n0}", statistics.SymlinkEnumeratedCount);
      Console.WriteLine("  Total source files size:  {0}", FormatHelpers.FormatSize(statistics.FileEnumeratedTotalSize));
      if (isDryRun) {
        Console.WriteLine("  Identical entries (Potential clones)");
        Console.WriteLine("    # of files to clone:    {0:n0}", statistics.FileClonedCount);
        Console.WriteLine("    Potential space savings:{0}", FormatHelpers.FormatSize(statistics.FileClonedTotalSize));
      } else {
        Console.WriteLine("  Cloned entries");
        Console.WriteLine("    # of files cloned:      {0:n0}", statistics.FileClonedCount);
        Console.WriteLine("    Total bytes cloned:     {0}", FormatHelpers.FormatSize(statistics.FileClonedTotalSize));
      }
      Console.WriteLine("  Already cloned entries");
      Console.WriteLine("    # of files:             {0:n0}", statistics.FileAlreadyClonedCount);
      Console.WriteLine("    Total bytes:            {0}", FormatHelpers.FormatSize(statistics.FileAlreadyClonedTotalSize));
      Console.WriteLine("  Skipped entries");
      Console.WriteLine("    # of files skipped:     {0:n0}", statistics.FileCloneSkippedCount);
      Console.WriteLine("    Total bytes skipped:    {0}", FormatHelpers.FormatSize(statistics.FileCloneSkippedTotalSize));
      Console.WriteLine("  # entries/sec:            {0:n0}",
        (statistics.DirectoryTraversedCount + statistics.FileClonedCount + statistics.FileAlreadyClonedCount + statistics.FileCloneSkippedCount) /
        statistics.ElapsedTime.TotalSeconds);
      Console.WriteLine("  # of errors:              {0:n0}", statistics.Errors.Count);
      DisplayErrors(statistics.Errors);
    }

    public static void DisplayGcStatistics(MtPoolFactory poolFactory) {
      ArgumentNullException.ThrowIfNull(poolFactory);

      Console.WriteLine();
      var sb = new StringBuilder();
      sb.AppendFormat("GC Memory: {0}", FormatHelpers.FormatSize(GC.GetTotalMemory(false)));
      for (var i = 0; i <= GC.MaxGeneration; i++) {
        sb.AppendFormat(", Gen{0}: {1:n0}", i, GC.CollectionCount(i));
      }

      var gcInfo = GC.GetGCMemoryInfo();
      sb.AppendFormat(", GC Pause: {0:N2} ms ({1:F2}%)",
        GC.GetTotalPauseDuration().TotalMilliseconds,
        gcInfo.PauseTimePercentage);
      sb.AppendFormat(", Total Allocated: {0}",
        FormatHelpers.FormatSize(GC.GetTotalAllocatedBytes()));

      Console.WriteLine(sb.ToString());

      DisplayPoolStatistics(poolFactory);
    }

    public static void DisplayPoolStatistics(MtPoolFactory poolFactory) {
      ArgumentNullException.ThrowIfNull(poolFactory);

      var pools = poolFactory.RegisteredPools;
      if (pools.Count == 0)
        return;

      var activePools = pools.Where(p => p.RentCount > 0 || p.CreatedCount > 0).ToList();
      if (activePools.Count == 0)
        return;

      int maxNameLength = Math.Max(activePools.Max(p => p.Name.Length), "Pool Name".Length);
      int nameWidth = maxNameLength;

      Console.WriteLine();
      Console.WriteLine("Pool Statistics:");
      
      string headerFormat = $"  {{0,-{nameWidth}}} {{1,10}} {{2,10}} {{3,10}} {{4,8}} {{5,10}}";
      Console.WriteLine(headerFormat, "Pool Name", "Rented", "Recycled", "Created", "Hit %", "In-Use");
      Console.WriteLine("  {0}", new string('-', nameWidth + 54));

      string rowFormat = $"  {{0,-{nameWidth}}} {{1,10:n0}} {{2,10:n0}} {{3,10:n0}} {{4,7:F1}}% {{5,10:n0}}";
      foreach (var pool in activePools.OrderBy(p => p.Name)) {
        Console.WriteLine(rowFormat,
          pool.Name,
          pool.RentCount,
          pool.ReturnCount,
          pool.CreatedCount,
          pool.HitRatio,
          pool.OutstandingCount);
      }
    }

    public static ProgressMode ParseProgressMode(string progressModeStr) {
      if (Enum.TryParse<ProgressMode>(progressModeStr, true, out var progressMode)) {
        return progressMode;
      }
      return ProgressMode.Default;
    }
  }
}