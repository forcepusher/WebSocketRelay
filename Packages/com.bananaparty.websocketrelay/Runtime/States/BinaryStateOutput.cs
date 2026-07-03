using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BananaParty.WebSocketRelay
{
    public class BinaryStateOutput : IStateOutput, IDisposable
    {
        private readonly MemoryStream _stream = new();
        private readonly BinaryWriter _buffer;
        private readonly Stack<bool> _inArrayStack = new();

        public BinaryStateOutput()
        {
            _buffer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
        }

        private bool InArray => _inArrayStack.Count > 0 && _inArrayStack.Peek();

        public ReadOnlyMemory<byte> GetBuffer() => _stream.GetBuffer().AsMemory(0, (int)_stream.Length);

        public void WriteByte(string name, byte value) => WriteEntry(name, value);

        public void WriteInt(string name, int value) => WriteEntry(name, value);

        public void WriteLong(string name, long value) => WriteEntry(name, value);

        public void WriteFloat(string name, float value) => WriteEntry(name, value);

        public void WriteDouble(string name, double value) => WriteEntry(name, value);

        public void WriteBool(string name, bool value) => WriteEntry(name, value);

        public void WriteString(string name, string value) => WriteEntry(name, value);

        public void WriteGuid(string name, Guid value) => WriteEntry(name, value);

        public byte[] ToArray() => _stream.ToArray();

        private void StartObject(string name)
        {
            WriteNameHash(name);
            _inArrayStack.Push(false);
        }

        private void EndObject()
        {
            if (_inArrayStack.Count > 0)
                _inArrayStack.Pop();
        }

        private void StartArray(string name)
        {
            WriteNameHash(name);
            _inArrayStack.Push(true);
        }

        private void EndArray()
        {
            if (_inArrayStack.Count > 0)
                _inArrayStack.Pop();
        }

        private void WriteEntry(string name, byte value)
        {
            WriteNameHash(InArray ? null : name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, int value)
        {
            WriteNameHash(InArray ? null : name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, long value)
        {
            WriteNameHash(InArray ? null : name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, float value)
        {
            WriteNameHash(InArray ? null : name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, double value)
        {
            WriteNameHash(InArray ? null : name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, bool value)
        {
            WriteNameHash(InArray ? null : name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, string value)
        {
            WriteNameHash(InArray ? null : name);
            byte[] stringBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            _buffer.Write((ushort)stringBytes.Length);
            _buffer.Write(stringBytes);
        }

        private void WriteEntry(string name, Guid value)
        {
            WriteNameHash(InArray ? null : name);
            _buffer.Write(value.ToByteArray());
        }

        private void WriteEntry(int value)
        {
            WriteNameHash(null);
            _buffer.Write(value);
        }

        private void WriteNameHash(string name)
        {
            _buffer.Write(Hash.StringToInt(name));
        }

        public void Dispose()
        {
            _buffer.Dispose();
            _stream.Dispose();
        }
    }
}
