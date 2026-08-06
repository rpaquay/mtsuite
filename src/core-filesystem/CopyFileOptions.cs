using System;

namespace mtsuite.CoreFileSystem;

[Flags]
public enum CopyFileOptions {
  Default = 0,
  /// <summary>
  /// Unbuffered copy, recommended for large files
  /// </summary>
  Unbuffered = 0x0001,
  /// <summary>
  /// Disable file cloning (CoW) on supported platforms (e.g. macOS APFS)
  /// </summary>
  NoClone = 0x0002,
}
