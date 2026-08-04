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
#nullable enable

using System;
using System.IO;
using mtsuite.CoreFileSystem.Utils;

namespace mtsuite.CoreFileSystem;

/// <summary>
/// Represents a fully qualified path stored as a single contiguous <see cref="string"/>,
/// with a parent path string and a file/directory name string.
/// </summary>
public readonly struct FullPath : IEquatable<FullPath>, IComparable<FullPath>
{
    /// <summary>
    /// Parent directory path string:
    /// <list type="bullet">
    ///   <item>
    ///     <term>Root Path (e.g. <c>"C:\"</c>, <c>"/"</c>):</term>
    ///     <description><c>null</c>, indicating this path is a root path with no parent.</description>
    ///   </item>
    ///   <item>
    ///     <term>Subpath / Child Path (e.g. <c>"C:\foo\bar"</c>, <c>"/usr/local/bin"</c>):</term>
    ///     <description>The parent path string (<c>"C:\foo"</c> or <c>"/usr/local"</c>).</description>
    ///   </item>
    ///   <item>
    ///     <term>Default / Uninitialized struct (<c>default(FullPath)</c>):</term>
    ///     <description><c>null</c>.</description>
    ///   </item>
    /// </list>
    /// </summary>
    private readonly string? _parent;

    /// <summary>
    /// The full, fully-qualified path string (e.g. <c>"C:\foo\bar"</c> or <c>"/usr/local/bin"</c>).
    /// Is <c>null</c> for uninitialized <c>default(FullPath)</c>.
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// The last segment (i.e. file name or directory name) of the full path,
    /// or <c>null</c> for root paths (e.g. <c>"C:\"</c> or <c>"/"</c>).
    /// </summary>
    private readonly string? _name;

    /// <summary>
    /// Construct a <see cref="FullPath"/> instance from a valid fully qualified path.
    /// Throws an exception if the <paramref name="path"/> is not valid.
    /// </summary>
    public FullPath(string path)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));
        if (!PathHelpers.IsPathAbsolute(path))
            throw new ArgumentException($"Path '{path}' should be absolute", nameof(path));
        if (PathHelpers.HasAltDirectorySeparators(path))
            throw new ArgumentException($"Path '{path}' should only contain valid directory separators", nameof(path));

        var parentPath = PathHelpers.GetParent(path);
        if (parentPath != null)
        {
            var info = PathHelpers.GetPathRootPrefixInfo(parentPath);
            if (parentPath.Length > info.Length && parentPath.EndsWith(Path.DirectorySeparatorChar))
            {
                parentPath = parentPath.Substring(0, parentPath.Length - 1);
            }
            _parent = parentPath;
            _name = PathHelpers.GetName(path);
        }
        else
        {
            _parent = null;
            _name = null;
        }

        _path = path;
    }

    /// <summary>
    /// Construct a <see cref="FullPath"/> from a parent path and a relative name.
    /// </summary>
    public FullPath(FullPath parent, string name)
        : this(parent.FullName, CombinePaths(parent.FullName, name.AsSpan()), name)
    {
    }

    /// <summary>
    /// Construct a <see cref="FullPath"/> from a parent path and a relative name slice.
    /// </summary>
    public FullPath(FullPath parent, StringSlice name)
        : this(parent.FullName, CombinePaths(parent.FullName, name.Span), name.ToString())
    {
    }

    /// <summary>
    /// Construct a <see cref="FullPath"/> from a parent path and a relative name span.
    /// </summary>
    public FullPath(FullPath parent, ReadOnlySpan<char> name)
        : this(parent.FullName, CombinePaths(parent.FullName, name), name.ToString())
    {
    }

    /// <summary>
    /// Construct a <see cref="FullPath"/> directly with a parent path string, path string, and name string.
    /// </summary>
    public FullPath(string? parent, string path, string? name)
    {
        _parent = parent;
        _path = path;
        _name = name;
    }

    public string FullName => _path ?? string.Empty;

    public string Name => _name ?? _path ?? string.Empty;

    public FullPath? Parent => _parent != null ? new FullPath(_parent) : null;

    public int Length => _path?.Length ?? 0;

    public bool IsEmpty => string.IsNullOrEmpty(_path);

    public bool HasTrailingSeparator => !string.IsNullOrEmpty(_path) && _path[_path.Length - 1] == Path.DirectorySeparatorChar;

    public PathHelpers.RootPrefixKind PathKind => string.IsNullOrEmpty(_path)
        ? PathHelpers.RootPrefixKind.None
        : PathHelpers.GetPathRootPrefixInfo(_path).RootPrefixKind;

    public FullPath Combine(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        return Combine(name.AsSpan());
    }

    public FullPath Combine(StringSlice name)
    {
        if (name.IsEmpty)
            throw new ArgumentNullException(nameof(name));

        if (!PathHelpers.HasDirectorySeparators(name.Span))
        {
            return new FullPath(this, name);
        }
        return Combine(name.Span);
    }

    public FullPath Combine(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty)
            throw new ArgumentNullException(nameof(name));

        if (!PathHelpers.HasDirectorySeparators(name))
        {
            return new FullPath(this, name);
        }

        var current = this;
        var remaining = name;
        while (!remaining.IsEmpty)
        {
            int nextSep = remaining.IndexOf(Path.DirectorySeparatorChar);
            if (nextSep < 0)
            {
                current = new FullPath(current, remaining);
                break;
            }
            else
            {
                var segment = remaining.Slice(0, nextSep);
                current = new FullPath(current, segment);
                remaining = remaining.Slice(nextSep + 1);
            }
        }
        return current;
    }

    private static string CombinePaths(string basePath, ReadOnlySpan<char> relative)
    {
        if (string.IsNullOrEmpty(basePath))
            throw new ArgumentNullException(nameof(basePath));
        if (relative.IsEmpty)
            throw new ArgumentNullException(nameof(relative));

        bool baseHasTrailing = basePath[basePath.Length - 1] == Path.DirectorySeparatorChar;
        if (baseHasTrailing)
        {
            return string.Concat(basePath.AsSpan(), relative);
        }
        else
        {
            return string.Concat(basePath.AsSpan(), PathHelpers.DirectorySeparatorString.AsSpan(), relative);
        }
    }

    public void CopyTo(StringBuffer sb)
    {
        if (!string.IsNullOrEmpty(_path))
        {
            sb.Append(_path);
        }
    }

    public override string ToString() => FullName;

    public bool Equals(FullPath other) =>
        string.Equals(_path, other._path, PathHelpers.FileNameComparison);

    public override bool Equals(object? obj) =>
        obj is FullPath other && Equals(other);

    public override int GetHashCode() =>
        _path != null ? PathHelpers.FileNameComparer.GetHashCode(_path) : 0;

    public int CompareTo(FullPath other) =>
        string.Compare(_path, other._path, PathHelpers.FileNameComparison);

    public static bool operator ==(FullPath left, FullPath right) => left.Equals(right);
    public static bool operator !=(FullPath left, FullPath right) => !left.Equals(right);
}
