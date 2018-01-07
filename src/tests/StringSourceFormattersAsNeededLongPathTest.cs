using System.Linq;
using mtsuite.CoreFileSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests {
  [TestClass]
  public class StringSourceFormattersAsNeededLongPathTest {
    public static string LongPath = string.Concat(Enumerable.Repeat(@"\bar\blah", 50));

    [TestMethod]
    public void FullPathShouldWork() {
      var path = new FullPath(@"c:\foo");
      var formatter = new PathSerializers.LongPathAsNeededSerializer(); ;
      Assert.AreEqual(@"c:\foo", formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void FullPathShouldWork2() {
      var path = new FullPath(@"c:\foo" + LongPath);
      var formatter = new PathSerializers.LongPathAsNeededSerializer(); ;
      Assert.AreEqual(@"\\?\c:\foo" + LongPath, formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void LongPathShouldWork1() {
      var path = new FullPath(@"\\?\c:\foo");
      var formatter = new PathSerializers.LongPathAsNeededSerializer(); ;
      Assert.AreEqual(@"c:\foo", formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void LongPathShouldWork2() {
      var path = new FullPath(@"\\?\c:\foo" + LongPath);
      var formatter = new PathSerializers.LongPathAsNeededSerializer(); ;
      Assert.AreEqual(@"\\?\c:\foo" + LongPath, formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void UncPathShouldWork1() {
      var path = new FullPath(@"\\server\foo");
      var formatter = new PathSerializers.LongPathAsNeededSerializer();
      Assert.AreEqual(@"\\server\foo", formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void UncPathShouldWork2() {
      var path = new FullPath(@"\\server\foo" + LongPath);
      var formatter = new PathSerializers.LongPathAsNeededSerializer();
      Assert.AreEqual(@"\\?\UNC\server\foo" + LongPath, formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void LongUncPathShouldWork1() {
      var path = new FullPath(@"\\?\UNC\server\foo");
      var formatter = new PathSerializers.LongPathAsNeededSerializer();
      Assert.AreEqual(@"\\server\foo", formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }

    [TestMethod]
    public void LongUncPathShouldWork2() {
      var path = new FullPath(@"\\?\UNC\server\foo" + LongPath);
      var formatter = new PathSerializers.LongPathAsNeededSerializer();
      Assert.AreEqual(@"\\?\UNC\server\foo" + LongPath, formatter.GetText(path));
      Assert.AreEqual(formatter.GetText(path).Length, formatter.GetLength(path));
    }
  }
}