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
using System.Runtime.InteropServices;

namespace mtsuite.shared.Utils;

public static class ConsoleSupport {
  private static readonly Lazy<bool> _isAnsiSupported = new(DetectAnsiSupport);

  /// <summary>
  /// Gets whether the current console environment supports ANSI / Virtual Terminal escape sequences.
  /// </summary>
  public static bool IsAnsiSupported => _isAnsiSupported.Value;

  public static bool DetectAnsiSupport() {
    // 1. If output is redirected (pipe, file), ANSI terminal sequences should not be emitted.
    if (Console.IsOutputRedirected) {
      return false;
    }

    // 2. Check standard NO_COLOR environment variable (https://no-color.org)
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) {
      return false;
    }

    // 3. Check for dumb terminal (common in minimalist or legacy environments)
    var term = Environment.GetEnvironmentVariable("TERM");
    if (string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    // 4. Windows-specific Virtual Terminal Processing detection and enablement
    if (OperatingSystem.IsWindows()) {
      return EnableWindowsVirtualTerminalProcessing();
    }

    // 5. Unix (Linux / macOS):
    // On Unix-like systems, when stdout is an interactive TTY and TERM is not dumb, ANSI VT100/xterm is standard.
    return true;
  }

  #region Windows P/Invoke

  private const int STD_OUTPUT_HANDLE = -11;
  private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern IntPtr GetStdHandle(int nStdHandle);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

  private static bool EnableWindowsVirtualTerminalProcessing() {
    try {
      var handle = GetStdHandle(STD_OUTPUT_HANDLE);
      if (handle == IntPtr.Zero || handle == new IntPtr(-1)) {
        return false;
      }

      if (!GetConsoleMode(handle, out var mode)) {
        return false;
      }

      // If already enabled, VT processing is supported
      if ((mode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0) {
        return true;
      }

      // Attempt to enable ENABLE_VIRTUAL_TERMINAL_PROCESSING
      var newMode = mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING;
      if (SetConsoleMode(handle, newMode)) {
        return true;
      }

      return false;
    } catch {
      // In case of unexpected native API errors
      return false;
    }
  }

  #endregion
}
