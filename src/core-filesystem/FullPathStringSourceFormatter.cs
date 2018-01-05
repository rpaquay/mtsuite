using mtsuite.CoreFileSystem.Utils;
using mtsuite.CoreFileSystem.Win32;

namespace mtsuite.CoreFileSystem {
  public class FullPathStringSourceFormatter : StringSourceFormatter<FullPath> {
    protected override int GetLengthImpl(FullPath source) {
      return source.Length;
    }

    protected override string GetTextImpl(FullPath source) {
      return source.FullName;
    }

    protected override void CopyToImpl(FullPath source, StringBuffer destination) {
      source.CopyTo(destination);
    }
  }
}