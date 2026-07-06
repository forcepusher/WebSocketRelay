using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class JsonStateInput : IStateInput
    {
        private readonly string _jsonString;
        private int _position;

        public JsonStateInput(string json)
        {
            _jsonString = json ?? "{}";
        }

        public string ReadString(string name)
        {
            AdvanceToEntry(name);

            if (_position < _jsonString.Length && _jsonString[_position] == '"')
                return ReadQuotedString();

            return ReadValueAsString();
        }

        public byte ReadByte(string name)
        {
            AdvanceToEntry(name);

            return ReadByteAtPosition();
        }

        public int ReadInt(string name)
        {
            AdvanceToEntry(name);

            return ReadIntAtPosition();
        }

        public long ReadLong(string name)
        {
            AdvanceToEntry(name);

            return ReadLongAtPosition();
        }

        public float ReadFloat(string name)
        {
            AdvanceToEntry(name);

            return ReadFloatAtPosition();
        }

        public double ReadDouble(string name)
        {
            AdvanceToEntry(name);

            return ReadDoubleAtPosition();
        }

        public bool ReadBool(string name)
        {
            AdvanceToEntry(name);

            return ReadBoolAtPosition();
        }

        public Vector2 ReadVector2(string name)
        {
            AdvanceToEntry(name);
            ReadObjectOpen();

            float x = ReadObjectComponentFloat("x");
            float y = ReadObjectComponentFloat("y");

            ReadObjectClose();
            return new Vector2(x, y);
        }

        public Vector3 ReadVector3(string name)
        {
            AdvanceToEntry(name);
            ReadObjectOpen();

            float x = ReadObjectComponentFloat("x");
            float y = ReadObjectComponentFloat("y");
            float z = ReadObjectComponentFloat("z");

            ReadObjectClose();
            return new Vector3(x, y, z);
        }

        public Vector2Int ReadVector2Int(string name)
        {
            AdvanceToEntry(name);
            ReadObjectOpen();

            int x = ReadObjectComponentInt("x");
            int y = ReadObjectComponentInt("y");

            ReadObjectClose();
            return new Vector2Int(x, y);
        }

        public Vector3Int ReadVector3Int(string name)
        {
            AdvanceToEntry(name);
            ReadObjectOpen();

            int x = ReadObjectComponentInt("x");
            int y = ReadObjectComponentInt("y");
            int z = ReadObjectComponentInt("z");

            ReadObjectClose();
            return new Vector3Int(x, y, z);
        }

        public Quaternion ReadQuaternion(string name)
        {
            AdvanceToEntry(name);
            ReadObjectOpen();

            float x = ReadObjectComponentFloat("x");
            float y = ReadObjectComponentFloat("y");
            float z = ReadObjectComponentFloat("z");
            float w = ReadObjectComponentFloat("w");

            ReadObjectClose();
            return new Quaternion(x, y, z, w);
        }

        public Guid ReadGuid(string name)
        {
            string value = ReadString(name);
            return Guid.TryParse(value, out Guid result) ? result : Guid.Empty;
        }

        private void AdvanceToEntry(string expectedName)
        {
            SkipItemSeparator();

            if (_position < _jsonString.Length && _jsonString[_position] == '{')
                _position++;

            string entryName = ReadQuotedString();
            if (!string.IsNullOrEmpty(expectedName) && entryName != expectedName)
                throw new KeyNotFoundException($"Expected field '{expectedName}' but found '{entryName ?? "null"}' in JSON state.");

            SkipColon();
        }

        private void ReadObjectOpen()
        {
            SkipWhitespace();
            if (_position >= _jsonString.Length || _jsonString[_position] != '{')
                throw new InvalidOperationException("Expected JSON object value.");

            _position++;
        }

        private void ReadObjectClose()
        {
            SkipWhitespace();
            if (_position < _jsonString.Length && _jsonString[_position] == '}')
                _position++;
        }

        private float ReadObjectComponentFloat(string componentName)
        {
            SkipItemSeparator();

            string entryName = ReadQuotedString();
            if (entryName != componentName)
                throw new KeyNotFoundException($"Expected field '{componentName}' but found '{entryName ?? "null"}' in JSON object.");

            SkipColon();
            return ReadFloatAtPosition();
        }

        private int ReadObjectComponentInt(string componentName)
        {
            SkipItemSeparator();

            string entryName = ReadQuotedString();
            if (entryName != componentName)
                throw new KeyNotFoundException($"Expected field '{componentName}' but found '{entryName ?? "null"}' in JSON object.");

            SkipColon();
            return ReadIntAtPosition();
        }

        private void SkipItemSeparator()
        {
            SkipWhitespace();
            if (_position < _jsonString.Length && _jsonString[_position] == ',')
                _position++;
            SkipWhitespace();
        }

        private void SkipColon()
        {
            SkipWhitespace();
            if (_position < _jsonString.Length && _jsonString[_position] == ':')
                _position++;
            SkipWhitespace();
        }

        private string ReadQuotedString()
        {
            if (_position >= _jsonString.Length || _jsonString[_position] != '"')
                return null;

            _position++;
            int start = _position;
            while (_position < _jsonString.Length && _jsonString[_position] != '"')
                _position++;

            string value = _jsonString.Substring(start, _position - start);
            if (_position < _jsonString.Length)
                _position++;

            return value;
        }

        private string ReadValueAsString()
        {
            SkipWhitespace();

            if (_position < _jsonString.Length && _jsonString[_position] == '"')
                return ReadQuotedString();

            int valueStart = _position;
            while (_position < _jsonString.Length && _jsonString[_position] != ',' && _jsonString[_position] != '}' && _jsonString[_position] != ']' && !char.IsWhiteSpace(_jsonString[_position]))
                _position++;

            return _jsonString.Substring(valueStart, _position - valueStart).Trim();
        }

        private byte ReadByteAtPosition()
        {
            string value = ReadValueAsString();
            return byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte result) ? result : (byte)0;
        }

        private int ReadIntAtPosition()
        {
            string value = ReadValueAsString();
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;
        }

        private long ReadLongAtPosition()
        {
            string value = ReadValueAsString();
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) ? result : 0L;
        }

        private float ReadFloatAtPosition()
        {
            string value = ReadValueAsString();
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : 0f;
        }

        private double ReadDoubleAtPosition()
        {
            string value = ReadValueAsString();
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : 0d;
        }

        private bool ReadBoolAtPosition()
        {
            string value = ReadValueAsString();
            if (!bool.TryParse(value, out bool result))
                return false;

            return result;
        }

        private void SkipWhitespace()
        {
            while (_position < _jsonString.Length && char.IsWhiteSpace(_jsonString[_position]))
                _position++;
        }
    }
}
