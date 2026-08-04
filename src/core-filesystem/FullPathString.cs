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
/// with a <see cref="FullPathStringReference"/> reference to its parent <see cref="FullPathString"/>.
/// </summary>
public readonly struct FullPathString : IEquatable<FullPathString>, IComparable<FullPathString>
{
    /// <summary>
    /// Reference to the parent directory path:
    /// <list type="bullet">
    ///   <item>
    ///     <term>Root Path (e.g. <c>"C:\"</c>, <c>"/"</c>):</term>
    ///     <description><c>null</c>, indicating this path is a root path with no parent.</description>
    ///   </item>
    ///   <item>
    ///     <term>Subpath / Child Path (e.g. <c>"C:\foo\bar"</c>, <c>"/usr/local/bin"</c>):</term>
    ///     <description>A non-null <see cref="FullPathStringReference"/> pointing to the parent directory.</description>
    ///   </item>
    ///   <item>
    ///     <term>Default / Uninitialized struct (<c>default(FullPathString)</c>):</term>
    ///     <description><c>null</c>.</description>
    ///   </item>
    /// </list>
    /// </summary>
    private readonly FullPathStringReference? _parent;

    /// <summary>
    /// The full, fully-qualified path string (e.g. <c>"C:\foo\bar"</c> or <c>"/usr/local/bin"</c>).
    /// Is <c>null</c> for uninitialized <c>default(FullPathString)</c>.
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// Construct a <see cref="FullPathString"/> instance from a valid fully qualified path.
    /// Throws an exception if the <paramref name="path"/> is not valid.
    /// </summary>
    public FullPathString(string path)
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
            _parent = new FullPathStringReference(new FullPathString(parentPath));
        }
        else
        {
            _parent = null;
        }

        _path = path;
    }

    /// <summary>
    /// Construct a <see cref="FullPathString"/> from a parent path and a relative name.
    /// </summary>
    public FullPathString(FullPathString parent, string name)
        : this(new FullPathStringReference(parent), CombinePaths(parent.FullName, name.AsSpan()))
    {
    }

    /// <summary>
    /// Construct a <see cref="FullPathString"/> from a parent path and a relative name slice.
    /// </summary>
    public FullPathString(FullPathString parent, StringSlice name)
        : this(new FullPathStringReference(parent), CombinePaths(parent.FullName, name.Span))
    {
    }

    /// <summary>
    /// Construct a <see cref="FullPathString"/> from a parent path and a relative name span.
    /// </summary>
    public FullPathString(FullPathString parent, ReadOnlySpan<char> name)
        : this(new FullPathStringReference(parent), CombinePaths(parent.FullName, name))
    {
    }

    /// <summary>
    /// Construct a <see cref="FullPathString"/> directly with a parent reference and path string.
    /// </summary>
    public FullPathString(FullPathStringReference? parent, string path)
    {
        _parent = parent;
        _path = path;
    }

    public FullPathStringReference? ParentReference => _parent;

    public string FullName => _path ?? string.Empty;

    public string Name => string.IsNullOrEmpty(_path) ? string.Empty : (PathHelpers.GetName(_path) ?? _path);

    public ReadOnlySpan<char> NameSpan => Name.AsSpan();

    public FullPathString? Parent => _parent?.FullPath;

    public int Length => _path?.Length ?? 0;

    public bool IsEmpty => string.IsNullOrEmpty(_path);

    public bool HasTrailingSeparator => !string.IsNullOrEmpty(_path) && _path[_path.Length - 1] == Path.DirectorySeparatorChar;

    public PathHelpers.RootPrefixKind PathKind => string.IsNullOrEmpty(_path)
        ? PathHelpers.RootPrefixKind.None
        : PathHelpers.GetPathRootPrefixInfo(_path).RootPrefixKind;

    public FullPathString Combine(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        return Combine(name.AsSpan());
    }

    public FullPathString Combine(StringSlice name)
    {
        if (name.IsEmpty)
            throw new ArgumentNullException(nameof(name));

        return Combine(name.Span);
    }

    public FullPathString Combine(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty)
            throw new ArgumentNullException(nameof(name));

        return new FullPathString(this, name);
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

    public bool Equals(FullPathString other) =>
        string.Equals(_path, other._path, PathHelpers.FileNameComparison);

    public override bool Equals(object? obj) =>
        obj is FullPathString other && Equals(other);

    public override int GetHashCode() =>
        _path != null ? PathHelpers.FileNameComparer.GetHashCode(_path) : 0;

    public int CompareTo(FullPathString other) =>
        string.Compare(_path, other._path, PathHelpers.FileNameComparison);

    public static bool operator ==(FullPathString left, FullPathString right) => left.Equals(right);
    public static bool operator !=(FullPathString left, FullPathString right) => !left.Equals(right);

    public static explicit operator FullPathString(FullPath path) => new FullPathString(path.FullName);
    public static explicit operator FullPath(FullPathString path) => new FullPath(path.FullName);

    public sealed class FullPathStringReference : IEquatable<FullPathStringReference>
    {
        public readonly FullPathString FullPath;

        public FullPathStringReference(FullPathString fullPath)
        {
            FullPath = fullPath;
        }

        public override bool Equals(object? obj) =>
            Equals(obj as FullPathStringReference);

        public bool Equals(FullPathStringReference? other)
        {
            if (other == null)
                return false;
            return FullPath.Equals(other.FullPath);
        }

        public override int GetHashCode() =>
            FullPath.GetHashCode();
    }
}
