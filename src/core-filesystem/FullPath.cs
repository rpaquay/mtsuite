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

using mtsuite.CoreFileSystem.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtsuite.CoreFileSystem {
  /// <summary>
  /// Represents a fully qualified path.
  /// </summary>
  public readonly struct FullPath : IEquatable<FullPath>, IComparable<FullPath> {
    private static readonly IPool<StringBuffer> FullNameBufferPool = new ConcurrentFixedSizeArrayPool<StringBuffer>(
      () => new StringBuffer(),
      sb => sb.Clear()
    );

    private static readonly StringSliceFactory NameSliceFactory = new StringSliceFactory();
      
    /// <summary>
    /// If there is a parent path, <see cref="_parent"/> the boxed instance of the parent <see cref="FullPath"/>.
    /// If there is no parent path, <see cref="_parent"/> is <code>null</code>, and <see cref="_name"/> is a root path.
    /// </summary>
    private readonly FullPathReference _parent;

    /// <summary>
    /// The "name" part (i.e file name or directory name) of the path, which may be the root path name (e.g. "C:\").
    /// </summary>
    private readonly StringSlice _name;

    /// <summary>
    /// Construct a <see cref="FullPath"/> instance from a valid fully qualifed path
    /// represented as the <see cref="string"/> <paramref name="path"/>.
    /// Throws an exception if the <paramref name="path"/> is not valid.
    /// </summary>
    public FullPath(string path) {
      if (!PathHelpers.IsPathAbsolute(path))
        ThrowArgumentException($"Path '{path}' should be absolute", "path");
      if (PathHelpers.HasAltDirectorySeparators(path))
        ThrowArgumentException($"Path '{path}' should only contain valid directory separators", "path");
      _parent = CreatePath(PathHelpers.GetParent(path));
      _name = _parent == null ? NameSliceFactory.Create(path) : NameSliceFactory.Create(PathHelpers.GetName(path));
    }

    /// <summary>
    /// Construct a <see cref="FullPath"/> instance from a valid parent <see cref="FullPath"/>
    /// and a relative name.
    /// Throws an exception if the <paramref name="name"/> is not valid.
    /// </summary>
    public FullPath(FullPath parent, string name) : this(parent, NameSliceFactory.Create(name)) {
    }

    /// <summary>
    /// Construct a <see cref="FullPath"/> instance from a valid parent <see cref="FullPath"/>
    /// and a relative name represented as a <see cref="StringSlice"/>.
    /// </summary>
    public FullPath(FullPath parent, StringSlice name) {
      if (parent._name.IsEmpty)
        ThrowArgumentNullException("parent");
      if (name.IsEmpty)
        ThrowArgumentNullException("name");
      if (PathHelpers.HasAltDirectorySeparators(name.Span) || PathHelpers.HasDirectorySeparators(name.Span))
        ThrowArgumentException("Name should not contain directory separators", "name");
      _parent = new FullPathReference(parent);
      _name = name;
    }

    private static FullPathReference CreatePath(string path) {
      if (path == null) {
        return null;
      }
      var parentPath = PathHelpers.GetParent(path);
      if (parentPath == null) {
        return new FullPathReference(new FullPath(path));
      }

      var name = PathHelpers.GetName(path);
      return new FullPathReference(new FullPath(parentPath).Combine(name));
    }

    private static void ThrowArgumentNullException(string paramName) {
      throw new ArgumentNullException(paramName);
    }

    private static void ThrowArgumentException(string message, string paramName) {
      throw new ArgumentException(message, paramName);
    }

    public string FullName {
      get {
        using var sb = FullNameBufferPool.AllocateFrom();
        BuildPath(sb.Item);
        return sb.Item.ToString();
      }
    }

    public string Name => _name.ToString();

    public StringSlice NameSlice => _name;

    public ReadOnlySpan<char> NameSpan => _name.Span;

    public FullPath? Parent => _parent?.FullPath;

    public FullPath Combine(string name) {
      if (string.IsNullOrEmpty(name)) {
        ThrowArgumentNullException("name");
      }
      return Combine(name.AsSpan());
    }

    public FullPath Combine(StringSlice name) {
      if (name.IsEmpty) {
        ThrowArgumentNullException("name");
      }

      if (!PathHelpers.HasDirectorySeparators(name.Span)) {
        return new FullPath(this, name);
      }
      return Combine(name.Span);
    }

    public FullPath Combine(ReadOnlySpan<char> name) {
      if (name.IsEmpty) {
        ThrowArgumentNullException("name");
      }

      if (!PathHelpers.HasDirectorySeparators(name)) {
        return new FullPath(this, NameSliceFactory.Create(name));
      }

      var current = this;
      var remaining = name;
      while (!remaining.IsEmpty) {
        int nextSep = remaining.IndexOf(Path.DirectorySeparatorChar);
        if (nextSep < 0) {
          current = new FullPath(current, NameSliceFactory.Create(remaining));
          break;
        } else {
          var segment = remaining.Slice(0, nextSep);
          current = new FullPath(current, NameSliceFactory.Create(segment));
          remaining = remaining.Slice(nextSep + 1);
        }
      }
      return current;
    }

    public bool HasTrailingSeparator {
      get { return !_name.IsEmpty && _name[_name.Length - 1] == Path.DirectorySeparatorChar; }
    }

    public PathHelpers.RootPrefixKind PathKind {
      get {
        if (_parent != null) {
          return _parent.FullPath.PathKind;
        }

        return PathHelpers.GetPathRootPrefixInfo(_name.ToString()).RootPrefixKind;
      }
    }

    public enum LongPathPrefixKind {
      None,
      LongDiskPath,
      LongUncPath,
    }

    private void BuildPath(StringBuffer sb) {
      if (_parent != null) {
        _parent.FullPath.BuildPath(sb);
        if (!_parent.FullPath.HasTrailingSeparator)
          sb.Append(PathHelpers.DirectorySeparatorString);
      }
      sb.Append(_name.Span);
    }

    public override string ToString() {
      return FullName;
    }

    public int Length {
      get { return GetLength(this); }
    }

    public void CopyTo(StringBuffer sb) {
      BuildPath(sb);
    }

    private static int GetLength(FullPath path) {
      var result = path._name.Length;
      while (path._parent != null) {
        path = path._parent.FullPath;
        result += path._name.Length;
        if (!path.HasTrailingSeparator)
          result++;
      }
      return result;
    }

    public bool Equals(FullPath other) {
      return Equals(_parent, other._parent) &&
             _name.Equals(other._name.Span, PathHelpers.FileNameComparison);
    }

    public override bool Equals(object obj) {
      if (obj is FullPath) {
        return Equals((FullPath)obj);
      }

      return false;
    }

    public override int GetHashCode() {
      unchecked {
        return ((_parent?.GetHashCode() ?? 0) * 397) ^
               _name.GetHashCode(PathHelpers.FileNameComparison);
      }
    }

    public int CompareTo(FullPath other) {
      return ComparePaths(this, other);
    }

    public static int ComparePaths(FullPath x, FullPath y) {
      if (ReferenceEquals(x._parent, y._parent)) {
        return x._name.CompareTo(y._name, PathHelpers.FileNameComparison);
      }

      int depthX = GetDepth(x);
      int depthY = GetDepth(y);

      FullPath xAligned = x;
      for (int i = depthX; i > depthY; i--) {
        xAligned = xAligned.Parent.Value;
      }

      FullPath yAligned = y;
      for (int i = depthY; i > depthX; i--) {
        yAligned = yAligned.Parent.Value;
      }

      int cmp = CompareEqualDepth(xAligned, yAligned);
      if (cmp != 0)
        return cmp;

      return depthX.CompareTo(depthY);
    }

    private static int CompareEqualDepth(FullPath x, FullPath y) {
      if (ReferenceEquals(x._parent, y._parent)) {
        return x._name.CompareTo(y._name, PathHelpers.FileNameComparison);
      }

      int parentCmp = CompareEqualDepth(x.Parent.Value, y.Parent.Value);
      if (parentCmp != 0)
        return parentCmp;

      return x._name.CompareTo(y._name, PathHelpers.FileNameComparison);
    }

    private static int GetDepth(FullPath path) {
      int depth = 0;
      for (FullPath? cur = path; cur != null; cur = cur.Value.Parent) {
        depth++;
      }
      return depth;
    }

    class FullPathReference : IEquatable<FullPathReference> {
      public readonly FullPath FullPath;

      public FullPathReference(FullPath fullPath) {
        FullPath = fullPath;
      }

      public override bool Equals(object obj) {
        return Equals(obj as FullPathReference);
      }

      public bool Equals(FullPathReference other) {
        if (other == null) {
          return false;
        }
        return Equals(this.FullPath, other.FullPath);
      }

      public override int GetHashCode() {
        return FullPath.GetHashCode();
      }
    }
  }
}
