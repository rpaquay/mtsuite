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
#nullable enable

using mtsuite.CoreFileSystem.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using mtsuite.CoreFileSystem.ObjectPool;

namespace mtsuite.CoreFileSystem {
  /// <summary>
  /// Represents a fully qualified path.
  /// </summary>
  public sealed class FullPath : IEquatable<FullPath>, IComparable<FullPath> {
    private static readonly IPool<StringBuffer> FullNameBufferPool = MtPoolFactory.Instance.Create(
      "FullPath.FullNameBuffer",
      static () => new StringBuffer(),
      static sb => sb.Clear()
    );
      
    /// <summary>
    /// If there is a parent path, <see cref="_parent"/> is a reference to the parent <see cref="FullPath"/>.
    /// If there is no parent path (root), <see cref="_parent"/> is null.
    /// </summary>
    private readonly FullPath? _parent;

    /// <summary>
    /// The "name" part (i.e file name or directory name) of the path, which may be the root path name (e.g. "C:\").
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// Construct a <see cref="FullPath"/> instance from a valid fully qualified path
    /// represented as the <see cref="string"/> <paramref name="path"/>.
    /// Throws an exception if the <paramref name="path"/> is not valid.
    /// </summary>
    public FullPath(string path) {
      if (path == null)
        ThrowArgumentNullException("path");
      if (!PathHelpers.IsPathAbsolute(path))
        ThrowArgumentException($"Path '{path}' should be absolute", "path");
      if (PathHelpers.HasAltDirectorySeparators(path))
        ThrowArgumentException($"Path '{path}' should only contain valid directory separators", "path");
      var parentPath = PathHelpers.GetParent(path);
      _parent = parentPath != null ? new FullPath(parentPath) : null;
      _name = _parent == null ? path : (PathHelpers.GetName(path) ?? string.Empty);
    }

    /// <summary>
    /// Construct a <see cref="FullPath"/> instance from a valid parent <see cref="FullPath"/>
    /// and a relative name.
    /// Throws an exception if the <paramref name="name"/> is not valid.
    /// </summary>
    public FullPath(FullPath parent, string name) {
      if (parent == null)
        ThrowArgumentNullException("parent");
      if (string.IsNullOrEmpty(name))
        ThrowArgumentNullException("name");
      if (PathHelpers.HasAltDirectorySeparators(name) || PathHelpers.HasDirectorySeparators(name))
        ThrowArgumentException("Name should not contain directory separators", "name");
      _parent = parent;
      _name = name;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowArgumentNullException(string paramName) {
      throw new ArgumentNullException(paramName);
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
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

    public string Name {
      get {
        return _name;
      }
    }

    public ReadOnlySpan<char> NameSpan => (_name ?? string.Empty).AsSpan();

    public FullPath? Parent {
      get {
        return _parent;
      }
    }

    public FullPath Combine(string name) {
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
        if (_parent != null) {
          return _parent.PathKind;
        }

        return PathHelpers.GetPathRootPrefixInfo(_name).RootPrefixKind;
      }
    }


    private void BuildPath(StringBuffer sb) {
      if (_parent != null) {
        _parent.BuildPath(sb);
        if (!_parent.HasTrailingSeparator) {
          char sep = _parent.PathKind == PathHelpers.RootPrefixKind.UnixPath ? '/' : '\\';
          sb.Append(sep);
        }
      }
      sb.Append(_name);
    }

    /// <summary>
    /// Attempts to compute the relative path from <paramref name="root"/> to this path.
    /// If this path is identical to <paramref name="root"/>, <paramref name="relativePath"/> is set to ".".
    /// If this path is a descendant of <paramref name="root"/>, <paramref name="relativePath"/> contains
    /// the relative path (e.g. "sub/file.txt").
    /// If this path is not a descendant of <paramref name="root"/>, returns false.
    /// </summary>
    public bool TryGetRelativePath(FullPath root, out string relativePath) {
      if (root == null) {
        relativePath = string.Empty;
        return false;
      }

      if (this == root) {
        relativePath = ".";
        return true;
      }

      using var sb = FullNameBufferPool.AllocateFrom();
      if (BuildRelativePath(sb.Item, root)) {
        relativePath = sb.Item.ToString();
        return true;
      }

      relativePath = string.Empty;
      return false;
    }

    private bool BuildRelativePath(StringBuffer sb, FullPath root) {
      if (this == root) {
        return true;
      }

      if (_parent != null && _parent.BuildRelativePath(sb, root)) {
        if (sb.Length > 0) {
          char sep = PathKind == PathHelpers.RootPrefixKind.UnixPath ? '/' : '\\';
          sb.Append(sep);
        }
        sb.Append(_name);
        return true;
      }

      return false;
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
      var cur = path._parent;
      while (cur != null) {
        result += cur._name.Length;
        if (!cur.HasTrailingSeparator)
          result++;
        cur = cur._parent;
      }
      return result;
    }

    public bool Equals(FullPath? other) {
      if (ReferenceEquals(this, other))
        return true;
      if (other is null)
        return false;
      return Equals(_parent, other._parent) &&
             string.Equals(_name, other._name, PathHelpers.FileNameComparison);
    }

    public override bool Equals(object? obj) {
      if (ReferenceEquals(this, obj))
        return true;
      if (obj is FullPath other) {
        return Equals(other);
      }

      return false;
    }

    public override int GetHashCode() {
      unchecked {
        return ((_parent != null ? _parent.GetHashCode() : 0) * 397) ^
               PathHelpers.FileNameComparer.GetHashCode(_name);
      }
    }

    public static bool operator ==(FullPath? left, FullPath? right) {
      if (ReferenceEquals(left, right))
        return true;
      if (left is null || right is null)
        return false;
      return left.Equals(right);
    }

    public static bool operator !=(FullPath? left, FullPath? right) => !(left == right);

    public int CompareTo(FullPath? other) {
      if (other is null)
        return 1;
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
      if (ReferenceEquals(x, y))
        return 0;
      if (x is null)
        return -1;
      if (y is null)
        return 1;

      // Fast path: if both paths share the exact same parent reference (e.g. sibling files in the same directory)
      // or are both root paths, we can directly compare their names in O(1) without computing depth or recursing.
      if (Equals(x._parent, y._parent)) {
        return string.Compare(x.Name, y.Name, PathHelpers.FileNameComparison);
      }

      int depthX = GetDepth(x);
      int depthY = GetDepth(y);

      // Walk back any excess depth on the deeper path so xAligned and yAligned have equal depths.
      FullPath xAligned = x;
      for (int i = depthX; i > depthY; i--) {
        xAligned = xAligned.Parent!;
      }

      FullPath yAligned = y;
      for (int i = depthY; i > depthX; i--) {
        yAligned = yAligned.Parent!;
      }

      // Compare segments from root down to the aligned depth.
      int cmp = CompareEqualDepth(xAligned, yAligned);
      if (cmp != 0)
        return cmp;

      // If the common prefix is identical, the path with fewer segments comes first.
      return depthX.CompareTo(depthY);
    }

    private static int CompareEqualDepth(FullPath x, FullPath y) {
      if (Equals(x._parent, y._parent)) {
        return string.Compare(x.Name, y.Name, PathHelpers.FileNameComparison);
      }

      int parentCmp = CompareEqualDepth(x.Parent!, y.Parent!);
      if (parentCmp != 0)
        return parentCmp;

      return string.Compare(x.Name, y.Name, PathHelpers.FileNameComparison);
    }

    private static int GetDepth(FullPath path) {
      int depth = 0;
      for (FullPath? cur = path; cur != null; cur = cur.Parent) {
        depth++;
      }
      return depth;
    }
  }
}
