using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class BinaryStateOutput : IStateOutput, IDisposable
    {
        private readonly MemoryStream _stream = new();
        private readonly BinaryWriter _buffer;

        public BinaryStateOutput()
        {
            _buffer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
        }

        public ReadOnlyMemory<byte> GetBuffer() => _stream.GetBuffer().AsMemory(0, (int)_stream.Length);

        public void BeginArrayProperty(string name) { }
        public void BeginArrayElement() { }
        public void EndArray() { }
        public void BeginObjectProperty(string name) { }
        public void BeginObjectElement() { }
        public void EndObject() { }

        public void WriteByte(string name, byte value) => WriteEntry(name, value);

        public void WriteInt(string name, int value) => WriteEntry(name, value);

        public void WriteLong(string name, long value) => WriteEntry(name, value);

        public void WriteFloat(string name, float value) => WriteEntry(name, value);

        public void WriteDouble(string name, double value) => WriteEntry(name, value);

        public void WriteBool(string name, bool value) => WriteEntry(name, value);

        public void WriteString(string name, string value) => WriteEntry(name, value);

        public void WriteVector2(string name, Vector2 value)
        {
            WriteNameHash(name);
            _buffer.Write(value.x);
            _buffer.Write(value.y);
        }

        public void WriteVector3(string name, Vector3 value)
        {
            WriteNameHash(name);
            _buffer.Write(value.x);
            _buffer.Write(value.y);
            _buffer.Write(value.z);
        }

        public void WriteVector2Int(string name, Vector2Int value)
        {
            WriteNameHash(name);
            _buffer.Write(value.x);
            _buffer.Write(value.y);
        }

        public void WriteVector3Int(string name, Vector3Int value)
        {
            WriteNameHash(name);
            _buffer.Write(value.x);
            _buffer.Write(value.y);
            _buffer.Write(value.z);
        }

        public void WriteQuaternion(string name, Quaternion value)
        {
            WriteNameHash(name);
            _buffer.Write(value.x);
            _buffer.Write(value.y);
            _buffer.Write(value.z);
            _buffer.Write(value.w);
        }

        public void WriteGuid(string name, Guid value) => WriteEntry(name, value);

        public byte[] ToArray() => _stream.ToArray();

        private void WriteEntry(string name, byte value)
        {
            WriteNameHash(name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, int value)
        {
            WriteNameHash(name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, long value)
        {
            WriteNameHash(name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, float value)
        {
            WriteNameHash(name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, double value)
        {
            WriteNameHash(name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, bool value)
        {
            WriteNameHash(name);
            _buffer.Write(value);
        }

        private void WriteEntry(string name, string value)
        {
            WriteNameHash(name);
            byte[] stringBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            _buffer.Write((ushort)stringBytes.Length);
            _buffer.Write(stringBytes);
        }

        private void WriteEntry(string name, Guid value)
        {
            WriteNameHash(name);
            _buffer.Write(value.ToByteArray());
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
