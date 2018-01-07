using mtsuite.CoreFileSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests {
  [TestClass]
  public class StringSourceFormattersAsIsTest {
    [TestMethod]
    public void FullPathShouldWork() {
      var path = new FullPath(@"c:\foo");
      var formatter = new PathSerializers.AsIsSerializer(); ;
      Assert.AreEqual(@"c:\foo", formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void LongPathShouldWork() {
      var path = new FullPath(@"\\?\c:\foo");
      var formatter = new PathSerializers.AsIsSerializer(); ;
      Assert.AreEqual(@"\\?\c:\foo", formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void UncPathShouldWork() {
      var path = new FullPath(@"\\server\foo");
      var formatter = new PathSerializers.AsIsSerializer();
      Assert.AreEqual(@"\\server\foo", formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void LongUncPathShouldWork() {
      var path = new FullPath(@"\\?\UNC\server\foo");
      var formatter = new PathSerializers.AsIsSerializer();
      Assert.AreEqual(@"\\?\UNC\server\foo", formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }
  }
}