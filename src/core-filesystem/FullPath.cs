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
  public struct FullPath : IEquatable<FullPath>, IComparable<FullPath> {
    private static readonly IPool<StringBuffer> FullNameBufferPool = new ConcurrentFixedSizeArrayPool<StringBuffer>(
      () => new StringBuffer(),
      sb => sb.Clear()
    );
      
    /// <summary>
    /// If there is a parent path, <see cref="_parent"/> is a lightweight index reference into
    /// <see cref="FullPathReferenceNoRelease"/>. If there is no parent path (root), <see cref="_parent.IsNull"/> is true.
    /// </summary>
    private readonly FullPathReference _parent;

    /// <summary>
    /// The "name" part (i.e file name or directory name) of the path, which may be the root path name (e.g. "C:\").
    /// </summary>
    private readonly string _name;

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
      _name = _parent.IsNull ? path : PathHelpers.GetName(path);
    }

    /// <summary>
    /// Construct a <see cref="FullPath"/> instance from a valid parent <see cref="FullPath"/>
    /// and a relative name.
    /// Throws an exception if the <paramref name="name"/> is not valid.
    /// </summary>
    public FullPath(FullPath parent, string name) {
      if (parent._name == null)
        ThrowArgumentNullException("parent");
      if (string.IsNullOrEmpty(name))
        ThrowArgumentNullException("name");
      if (PathHelpers.HasAltDirectorySeparators(name) || PathHelpers.HasDirectorySeparators(name))
        ThrowArgumentException("Name should not contain directory separators", "name");
      _parent = FullPathReferenceNoRelease.Allocate(parent);
      _name = name;
    }

    public FullPath(FullPathReference parent, string name) {
      if (string.IsNullOrEmpty(name))
        ThrowArgumentNullException("name");
      _parent = parent;
      _name = name;
    }

    private static FullPathReference CreatePath(string path) {
      if (path == null) {
        return default;
      }
      return FullPathReferenceNoRelease.Allocate(new FullPath(path));
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

    public string GetFullName(INameTable nameTable) {
      using var sb = FullNameBufferPool.AllocateFrom();
      BuildPath(sb.Item);
      return nameTable.GetOrAdd(sb.Item.ToSpan());
    }

    public string Name {
      get {
        return _name;
      }
    }

    public ReadOnlySpan<char> NameSpan => (_name ?? string.Empty).AsSpan();

    public FullPath? Parent {
      get {
        if (_parent.IsNull)
          return null;
        return _parent.FullPath;
      }
    }

    public readonly FullPath Combine(string name) {
      if (string.IsNullOrEmpty(name)) {
        ThrowArgumentNullException("name");
      }

      if (name.IndexOf('\\') < 0 && name.IndexOf('/') < 0) {
        return new FullPath(this, name);
      }
      var current = this;
      foreach (var segment in SplitRelativePath(name)) {
        current = new FullPath(current, segment);
      }
      return current;
    }

    private static IEnumerable<string> SplitRelativePath(string name) {
      var index = 0;
      while (index < name.Length) {
        int nextSep = -1;
        for (int i = index; i < name.Length; i++) {
          if (name[i] == '\\' || name[i] == '/') {
            nextSep = i;
            break;
          }
        }
        if (nextSep < 0) {
          yield return name.Substring(index);
          index = name.Length;
        } else {
          yield return name.Substring(index, nextSep - index);
          index = nextSep + 1;
        }
      }
    }

    public bool HasTrailingSeparator {
      get { return _name != null && _name.Length > 0 && (_name[_name.Length - 1] == '\\' || _name[_name.Length - 1] == '/'); }
    }

    public PathHelpers.RootPrefixKind PathKind {
      get {
        if (!_parent.IsNull) {
          return _parent.FullPath.PathKind;
        }

        return PathHelpers.GetPathRootPrefixInfo(_name).RootPrefixKind;
      }
    }

    public enum LongPathPrefixKind {
      None,
      LongDiskPath,
      LongUncPath,
    }

    private void BuildPath(StringBuffer sb) {
      if (!_parent.IsNull) {
        _parent.FullPath.BuildPath(sb);
        if (!_parent.FullPath.HasTrailingSeparator) {
          char sep = _parent.FullPath.PathKind == PathHelpers.RootPrefixKind.UnixPath ? '/' : '\\';
          sb.Append(sep);
        }
      }
      sb.Append(_name);
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
      while (!path._parent.IsNull) {
        path = path._parent.FullPath;
        result += path._name.Length;
        if (!path.HasTrailingSeparator)
          result++;
      }
      return result;
    }

    public bool Equals(FullPath other) {
      return _parent == other._parent &&
             string.Equals(_name, other._name, PathHelpers.FileNameComparison);
    }

    public override bool Equals(object obj) {
      if (obj is FullPath) {
        return Equals((FullPath)obj);
      }

      return false;
    }

    public override int GetHashCode() {
      unchecked {
        return (_parent.GetHashCode() * 397) ^
               PathHelpers.FileNameComparer.GetHashCode(_name);
      }
    }

    public int CompareTo(FullPath other) {
      return ComparePaths(this, other);
    }

    /// <summary>
    /// Compares two <see cref="FullPath"/> instances hierarchically segment-by-segment from root to leaf.
    /// Performance note: Rather than allocating List or array objects on the heap, this method aligns
    /// both paths by depth and compares their segments recursively from root down to the common depth.
    /// This achieves zero heap allocations and eliminates GC pressure during sorting and comparisons
    /// by using the CPU call stack (typically only 3 to 10 frames deep) instead of heap memory.
    /// </summary>
    public static int ComparePaths(FullPath x, FullPath y) {
      // Fast path: if both paths share the exact same parent reference (e.g. sibling files in the same directory)
      // or are both root paths, we can directly compare their names in O(1) without computing depth or recursing.
      if (x._parent == y._parent) {
        return string.Compare(x.Name, y.Name, PathHelpers.FileNameComparison);
      }

      int depthX = GetDepth(x);
      int depthY = GetDepth(y);

      // Walk back any excess depth on the deeper path so xAligned and yAligned have equal depths.
      FullPath xAligned = x;
      for (int i = depthX; i > depthY; i--) {
        // ReSharper disable once PossibleInvalidOperationException
        xAligned = xAligned.Parent.Value;
      }

      FullPath yAligned = y;
      for (int i = depthY; i > depthX; i--) {
        // ReSharper disable once PossibleInvalidOperationException
        yAligned = yAligned.Parent.Value;
      }

      // Compare segments from root down to the aligned depth.
      int cmp = CompareEqualDepth(xAligned, yAligned);
      if (cmp != 0)
        return cmp;

      // If the common prefix is identical, the path with fewer segments comes first.
      return depthX.CompareTo(depthY);
    }

    private static int CompareEqualDepth(FullPath x, FullPath y) {
      if (x._parent == y._parent) {
        return string.Compare(x.Name, y.Name, PathHelpers.FileNameComparison);
      }

      // ReSharper disable twice PossibleInvalidOperationException
      int parentCmp = CompareEqualDepth(x.Parent.Value, y.Parent.Value);
      if (parentCmp != 0)
        return parentCmp;

      return string.Compare(x.Name, y.Name, PathHelpers.FileNameComparison);
    }

    private static int GetDepth(FullPath path) {
      int depth = 0;
      for (FullPath? cur = path; cur != null; cur = cur.Value.Parent) {
        depth++;
      }
      return depth;
    }

    public FullPathReference ToFullPathReference() {
      return FullPathReferenceNoRelease.Allocate(this);
    }
  }
}
