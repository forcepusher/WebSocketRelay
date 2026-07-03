using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class BinaryStateInput : IStateInput
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _pos;

        public BinaryStateInput(ReadOnlyMemory<byte> data)
        {
            _data = data;
        }

        public string ReadString(string name)
        {
            VerifyEntryName(name);
            return ReadStringValue();
        }

        public byte ReadByte(string name)
        {
            VerifyEntryName(name);
            return ReadByteValue();
        }

        public int ReadInt(string name)
        {
            VerifyEntryName(name);
            return ReadInt32();
        }

        public long ReadLong(string name)
        {
            VerifyEntryName(name);
            return ReadInt64();
        }

        public float ReadFloat(string name)
        {
            VerifyEntryName(name);
            return ReadFloat32();
        }

        public double ReadDouble(string name)
        {
            VerifyEntryName(name);
            return ReadFloat64();
        }

        public bool ReadBool(string name)
        {
            VerifyEntryName(name);
            return ReadBoolValue();
        }

        public Vector2 ReadVector2(string name)
        {
            VerifyEntryName(name);
            return new Vector2(ReadFloat32(), ReadFloat32());
        }

        public Vector3 ReadVector3(string name)
        {
            VerifyEntryName(name);
            return new Vector3(ReadFloat32(), ReadFloat32(), ReadFloat32());
        }

        public Vector2Int ReadVector2Int(string name)
        {
            VerifyEntryName(name);
            return new Vector2Int(ReadInt32(), ReadInt32());
        }

        public Vector3Int ReadVector3Int(string name)
        {
            VerifyEntryName(name);
            return new Vector3Int(ReadInt32(), ReadInt32(), ReadInt32());
        }

        public Quaternion ReadQuaternion(string name)
        {
            VerifyEntryName(name);
            return new Quaternion(ReadFloat32(), ReadFloat32(), ReadFloat32(), ReadFloat32());
        }

        public Guid ReadGuid(string name)
        {
            VerifyEntryName(name);
            return ReadGuidValue();
        }

        private void VerifyEntryName(string expectedName)
        {
            VerifyNameHash(expectedName);
        }

        private void VerifyNameHash(string expectedName)
        {
            int nameHash = ReadNameHash();
            int expectedHash = Hash.StringToInt(expectedName);

            if (nameHash != expectedHash)
            {
                throw new InvalidDataException(
                    $"Name hash mismatch. Expected '{expectedName ?? string.Empty}' ({expectedHash}), got {nameHash}.");
            }
        }

        private int ReadNameHash()
        {
            if (_pos + 4 > _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading name hash.");

            int hash = BitConverter.ToInt32(_data.Span.Slice(_pos, 4));
            _pos += 4;
            return hash;
        }

        private byte ReadByteValue()
        {
            if (_pos >= _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading byte value.");

            return _data.Span[_pos++];
        }

        private int ReadInt32()
        {
            if (_pos + 4 > _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading Int32.");

            int value = BitConverter.ToInt32(_data.Span.Slice(_pos, 4));
            _pos += 4;
            return value;
        }

        private long ReadInt64()
        {
            if (_pos + 8 > _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading Int64.");

            long value = BitConverter.ToInt64(_data.Span.Slice(_pos, 8));
            _pos += 8;
            return value;
        }

        private float ReadFloat32()
        {
            if (_pos + 4 > _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading Float32.");

            float value = BitConverter.ToSingle(_data.Span.Slice(_pos, 4));
            _pos += 4;
            return value;
        }

        private double ReadFloat64()
        {
            if (_pos + 8 > _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading Float64.");

            double value = BitConverter.ToDouble(_data.Span.Slice(_pos, 8));
            _pos += 8;
            return value;
        }

        private bool ReadBoolValue()
        {
            if (_pos >= _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading boolean.");

            return _data.Span[_pos++] != 0;
        }

        private string ReadStringValue()
        {
            if (_pos + 2 > _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading string length.");

            ushort length = BitConverter.ToUInt16(_data.Span.Slice(_pos, 2));
            _pos += 2;

            if (length == 0)
                return string.Empty;

            if (_pos + length > _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading string content.");

            string value = Encoding.UTF8.GetString(_data.Span.Slice(_pos, length));
            _pos += length;
            return value;
        }

        private Guid ReadGuidValue()
        {
            if (_pos + 16 > _data.Length)
                throw new EndOfStreamException("Unexpected end of binary stream while reading Guid.");

            ReadOnlySpan<byte> guidBytes = _data.Span.Slice(_pos, 16);
            _pos += 16;
            return new Guid(guidBytes);
        }
    }
}
