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
using System.Text;
using mtsuite.shared.Collections;

namespace mtsuite.shared.Win32 {
  public class ByteBuffer : IDisposable {
    private readonly SafeHGlobalHandle _memoryHandle = new SafeHGlobalHandle();
    private int _capacity;

    public ByteBuffer(int capacity) {
      if (capacity <= 0) {
        throw new ArgumentException();
      }
      Allocate(capacity);
    }

    private void Allocate(int capacity) {
      _memoryHandle.Realloc(capacity);
      _capacity = capacity;
    }

    public int Capacity {
      get { return _capacity; }
      set { Allocate(value); }
    }

    public IntPtr Pointer {
      get {
        if (_memoryHandle == null) {
          throw new InvalidOperationException("Buffer is not allocated");
        }
        return _memoryHandle.Pointer;
      }
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public sbyte Read<TStruct>(TStruct source, Expression<Func<TStruct, sbyte>> field) {
      var offset = GetFieldOffset(source, field);
      var value = ReadFromMemory(offset, Marshal.SizeOf(typeof(sbyte)));
      return (sbyte)value;
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public byte Read<TStruct>(TStruct source, Expression<Func<TStruct, byte>> field) {
      var offset = GetFieldOffset(source, field);
      var value = ReadFromMemory(offset, Marshal.SizeOf(typeof(byte)));
      return (byte)value;
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public short Read<TStruct>(TStruct source, Expression<Func<TStruct, short>> field) {
      var offset = GetFieldOffset(source, field);
      var value = ReadFromMemory(offset, Marshal.SizeOf(typeof(short)));
      return (short)value;
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public ushort Read<TStruct>(TStruct source, Expression<Func<TStruct, ushort>> field) {
      var offset = GetFieldOffset(source, field);
      var value = ReadFromMemory(offset, Marshal.SizeOf(typeof(ushort)));
      return (ushort)value;
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public int Read<TStruct>(TStruct source, Expression<Func<TStruct, int>> field) {
      var offset = GetFieldOffset(source, field);
      var value = ReadFromMemory(offset, Marshal.SizeOf(typeof(int)));
      return (int)value;
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public uint Read<TStruct>(TStruct source, Expression<Func<TStruct, uint>> field) {
      var offset = GetFieldOffset(source, field);
      var value = ReadFromMemory(offset, Marshal.SizeOf(typeof(uint)));
      return (uint)value;
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public long Read<TStruct>(TStruct source, Expression<Func<TStruct, long>> field) {
      var offset = GetFieldOffset(source, field);
      var value = ReadFromMemory(offset, Marshal.SizeOf(typeof(long)));
      return value;
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public ulong Read<TStruct>(TStruct source, Expression<Func<TStruct, ulong>> field) {
      var offset = GetFieldOffset(source, field);
      var value = ReadFromMemory(offset, Marshal.SizeOf(typeof(ulong)));
      return (ulong)value;
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public TField Read<TStruct, TField>(TStruct source, Expression<Func<TStruct, TField>> field) {
      var offset = GetFieldOffset(source, field);
      var fieldType = typeof(TField);
      if (fieldType.IsEnum) {
        fieldType = fieldType.GetEnumUnderlyingType();
      }
      var size = Marshal.SizeOf(fieldType);
      var value = ReadFromMemory(offset, size);

      if (fieldType == typeof(byte))
        return (TField)(object)(byte)value;

      if (fieldType == typeof(sbyte))
        return (TField)(object)(sbyte)value;

      if (fieldType == typeof(ushort))
        return (TField)(object)(ushort)value;

      if (fieldType == typeof(short))
        return (TField)(object)(short)value;

      if (fieldType == typeof(int))
        return (TField)(object)(int)value;

      if (fieldType == typeof(uint))
        return (TField)(object)(uint)value;

      if (fieldType == typeof(long))
        return (TField)(object)value;

      if (fieldType == typeof(ulong))
        return (TField)(object)(ulong)value;

      throw new InvalidOperationException("Invalid integer type");
    }

    public unsafe string ReadString(int offset, int length) {
      CheckRange(offset, length);
      var bufferStart = (char*)(Pointer + offset).ToPointer();
      return new string(bufferStart, 0, length);
    }

    private unsafe Int64 ReadFromMemory(int offset, int size) {
      CheckRange(offset, size);

      byte* bufferStart = ((byte*)Pointer) + offset;
      switch (size) {
        case 1:
          return *bufferStart;
        case 2:
          return *(short*)bufferStart;
        case 4:
          return *(int*)bufferStart;
        case 8:
          return *(long*)bufferStart;
      }
      throw new InvalidOperationException("Invalid field size (must be 1, 2, 4 or 8)");
    }

    /// <summary>
    /// Read any integer (or enum) value from 1 byte (byte) to 8 bytes (long)
    /// </summary>
    public void Write<TStruct, TField>(TStruct source, Expression<Func<TStruct, TField>> field, long value) {
      var offset = GetFieldOffset(source, field);
      var fieldType = typeof(TField);
      if (fieldType.IsEnum) {
        fieldType = fieldType.GetEnumUnderlyingType();
      }

      if (fieldType == typeof(byte)) {
        WriteUInt8(offset, unchecked((byte)value));

      } else if (fieldType == typeof(sbyte)) {
        WriteUInt8(offset, unchecked((byte)value));

      } else if (fieldType == typeof(short)) {
        WriteUInt16(offset, unchecked((ushort)value));

      } else if (fieldType == typeof(ushort)) {
        WriteUInt16(offset, unchecked((ushort)value));

      } else if (fieldType == typeof(int)) {
        WriteUInt32(offset, unchecked((uint)value));

      } else if (fieldType == typeof(uint)) {
        WriteUInt32(offset, unchecked((uint)value));

      } else if (fieldType == typeof(long)) {
        WriteUInt64(offset, unchecked((ulong)value));

      } else if (fieldType == typeof(ulong)) {
        WriteUInt64(offset, unchecked((ulong)value));

      } else {
        throw new InvalidOperationException("Invalid integer type");
      }
    }

    private unsafe void WriteUInt8(int offset, byte value) {
      EnsureCapacity(offset, sizeof(byte));

      byte* bufferStart = (byte*)(Pointer + offset).ToPointer();
      (*bufferStart) = value;
    }

    private unsafe void WriteUInt16(int offset, ushort value) {
      EnsureCapacity(offset, sizeof(ushort));

      ushort* bufferStart = (ushort*)(Pointer + offset).ToPointer();
      (*bufferStart) = value;
    }

    private unsafe void WriteUInt32(int offset, uint value) {
      EnsureCapacity(offset, sizeof(uint));

      uint* bufferStart = (uint*)(Pointer + offset).ToPointer();
      (*bufferStart) = value;
    }

    private unsafe void WriteUInt64(int offset, ulong value) {
      EnsureCapacity(offset, sizeof(ulong));

      ulong* bufferStart = (ulong*)(Pointer + offset).ToPointer();
      (*bufferStart) = value;
    }

    public unsafe void WriteString(int offset, int count, StringBuffer stringBuffer) {
      EnsureCapacity(offset, count * sizeof(char));
      char* bufferStart = (char*)(Pointer + offset).ToPointer();
      for (var i = 0; i < count; i++) {
        bufferStart[i] = stringBuffer.Data[i];
      }
    }

    public int GetFieldOffset<TStruct, TField>(TStruct source, Expression<Func<TStruct, TField>> field) {
      var name = ReflectionUtils.GetFieldName(source, field);
      return Marshal.OffsetOf(typeof(TStruct), name).ToInt32();
    }

    public int GetFieldSize<TStruct, TField>(TStruct source, Expression<Func<TStruct, TField>> field) {
      return Marshal.SizeOf(typeof(TField));
    }

    private void CheckRange(int offset, int size) {
      if (offset < 0)
        ThrowInvalidRange(offset, size);
      if (size < 0)
        ThrowInvalidRange(offset, size);
      if (checked(offset + size) > Capacity)
        ThrowInvalidRange(offset, size);
    }

    private void EnsureCapacity(int offset, int size) {
      if (offset < 0)
        ThrowInvalidRange(offset, size);
      if (size < 0)
        ThrowInvalidRange(offset, size);

      checked {
        if (_capacity >= offset + size)
          return;

        var newCapacity = _capacity;
        while (newCapacity < offset + size) {
          newCapacity *= 2;
        }
        Allocate(newCapacity);
      }
    }

    private void ThrowInvalidRange(int offset, int size) {
      throw new InvalidOperationException(string.Format("Trying to read past end of buffer (Offset={0}, Size={1}, Capacity={2}", offset, size, Capacity));
    }

    public void Dispose() {
      _memoryHandle.Dispose();
    }

    public override string ToString() {
      var sb = new StringBuilder();
      sb.AppendFormat("Capacity={0}, [", Capacity);
      for (var i = 0; i < Capacity; i++) {
        sb.AppendFormat("0x{0:X2}, ", (byte)ReadFromMemory(i, 1));
      }
      sb.AppendFormat("]");
      return sb.ToString();
    }
  }
}