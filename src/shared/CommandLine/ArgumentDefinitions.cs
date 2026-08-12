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

using System;
using System.Collections.Generic;

namespace mtsuite.shared.CommandLine {
  public class LazyDefault<T> {
    private readonly Func<T> _factory;
    public LazyDefault(T value) {
      _factory = () => value;
    }
    public LazyDefault(Func<T> factory) {
      _factory = factory;
    }
    public T Value => _factory();
    public static implicit operator LazyDefault<T>(T value) => value == null ? null : new LazyDefault<T>(value);
    public static implicit operator LazyDefault<T>(Func<T> factory) => factory == null ? null : new LazyDefault<T>(factory);
  }

  /// <summary>
  /// Base class of all command line argument definitions.
  /// </summary>
  public abstract class ArgDef {
    public string Id { get; set; }
    public string Description { get; set; }
    public bool IsMandatory { get; set; }
  }

  /// <summary>
  /// Base class for arguments which have a name (e.g. "/h", "/d:2").
  /// </summary>
  public abstract class NameArgDef : ArgDef {
    public string ShortName { get; set; }
    public string AltShortName { get; set; }
    public string LongName { get; set; }
  }

  /// <summary>
  /// Base class for named arguments which take a value (e.g. "/d:2").
  /// </summary>
  public abstract class OptionArgDef : NameArgDef {
    public string ValueName { get; set; }
    public object DefaultValue { get; set; }
  }

  /// <summary>
  /// Definition for positional arguments (no prefix name, e.g. a filename).
  /// </summary>
  public class PositionalArgDef : ArgDef {
    public LazyDefault<string> DefaultValue { get; set; }
  }

  /// <summary>
  /// Definition for multiple positional arguments.
  /// </summary>
  public class MultiplePositionalArgDef : ArgDef {
    public LazyDefault<IList<string>> DefaultValue { get; set; }
  }

  /// <summary>
  /// Definition for named boolean arguments (e.g. "/h").
  /// </summary>
  public class FlagArgDef : NameArgDef {
    public static object ValueMarker = new object();
  }

  /// <summary>
  /// Definition for named arguments which have an integer value (e.g. "/d:2").
  /// </summary>
  public class IntOptionArgDef : OptionArgDef {
    public Func<int, string> Validator { get; set; }
    public Func<string, int?> StringParser { get; set; }
  }

  /// <summary>
  /// Definition for named arguments which have a string value (e.g. "/a:foo").
  /// </summary>
  public class StringOptionArgDef : OptionArgDef {
    public Func<string, string> Validator { get; set; }
  }
}