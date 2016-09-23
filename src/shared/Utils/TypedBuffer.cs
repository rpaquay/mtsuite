// Copyright 2016 Renaud Paquay All Rights Reserved.
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
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using mtsuite.shared.Collections;

namespace mtsuite.shared.Utils {
  public struct TypedBuffer<TStruct> {
    private readonly ByteBuffer _buffer;

    public TypedBuffer(ByteBuffer buffer) {
      _buffer = buffer;
    }

    public int SizeOf {
      get { return Marshal.SizeOf(typeof(TStruct)); }
    }

    public int GetFieldOffset<TField>(Expression<Func<TStruct, TField>> field) {
      return _buffer.GetFieldOffset(default(TStruct), field);
    }

    public int GetFieldSize<TField>(Expression<Func<TStruct, TField>> field) {
      return _buffer.GetFieldSize(default(TStruct), field);
    }

    public sbyte Read(Expression<Func<TStruct, sbyte>> field) {
      return _buffer.Read(default(TStruct), field);
    }

    public byte Read(Expression<Func<TStruct, byte>> field) {
      return _buffer.Read(default(TStruct), field);
    }

    public short Read(Expression<Func<TStruct, short>> field) {
      return _buffer.Read(default(TStruct), field);
    }

    public ushort Read(Expression<Func<TStruct, ushort>> field) {
      return _buffer.Read(default(TStruct), field);
    }

    public int Read(Expression<Func<TStruct, int>> field) {
      return _buffer.Read(default(TStruct), field);
    }

    public uint Read(Expression<Func<TStruct, uint>> field) {
      return _buffer.Read(default(TStruct), field);
    }

    public long Read(Expression<Func<TStruct, long>> field) {
      return _buffer.Read(default(TStruct), field);
    }

    public ulong Read(Expression<Func<TStruct, ulong>> field) {
      return _buffer.Read(default(TStruct), field);
    }

    public TField Read<TField>(Expression<Func<TStruct, TField>> field) {
      return _buffer.Read(default(TStruct), field);
    }

    public string ReadString(int offset, int length) {
      return _buffer.ReadString(offset, length);
    }

    public void Write<TField>(Expression<Func<TStruct, TField>> field, long value) {
      _buffer.Write(default(TStruct), field, value);
    }

    public void WriteString(int offset, int size, StringBuffer stringBuffer) {
      _buffer.WriteString(offset, size, stringBuffer);
    }
  }
}