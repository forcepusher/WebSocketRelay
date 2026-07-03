using System;
using System.Collections.Generic;
using System.Globalization;

namespace BananaParty.WebSocketRelay
{
    public class JsonStateInput : IStateInput
    {
        private readonly string _jsonString;
        private int _position;
        private readonly Stack<bool> _inArrayStack = new();

        public JsonStateInput(string json)
        {
            _jsonString = json ?? "{}";
        }

        private bool InArray => _inArrayStack.Count > 0 && _inArrayStack.Peek();

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

        public Guid ReadGuid(string name)
        {
            string value = ReadString(name);
            return Guid.TryParse(value, out Guid result) ? result : Guid.Empty;
        }

        private void StartObject(string name)
        {
            AdvanceIntoContainer('{', name);
            _inArrayStack.Push(false);
        }

        private void EndObject()
        {
            ReadContainerClose('}');
            if (_inArrayStack.Count > 0)
                _inArrayStack.Pop();
        }

        private void StartArray(string name)
        {
            AdvanceIntoContainer('[', name);
            _inArrayStack.Push(true);
        }

        private void EndArray()
        {
            ReadContainerClose(']');
            if (_inArrayStack.Count > 0)
                _inArrayStack.Pop();
        }

        private bool HasNextArrayElement()
        {
            SkipWhitespace();
            return _position < _jsonString.Length && _jsonString[_position] != ']';
        }

        private void AdvanceIntoContainer(char open, string name)
        {
            if (!InArray && !string.IsNullOrEmpty(name))
            {
                SkipWhitespace();
                if (_position < _jsonString.Length && _jsonString[_position] == open)
                    _position++;

                SkipItemSeparator();
                ReadContainerName(name);
                SkipColon();
            }
            else if (InArray)
            {
                SkipItemSeparator();
            }

            SkipWhitespace();
            if (_position < _jsonString.Length && _jsonString[_position] == open)
                _position++;
        }

        private void ReadContainerClose(char close)
        {
            SkipWhitespace();
            if (_position < _jsonString.Length && _jsonString[_position] == close)
                _position++;
        }

        private void ReadContainerName(string expectedName)
        {
            ReadQuotedString();
        }

        private void AdvanceToEntry(string expectedName)
        {
            SkipItemSeparator();
            if (InArray) return;

            string entryName = ReadQuotedString();
            if (!string.IsNullOrEmpty(expectedName) && entryName != expectedName)
                throw new KeyNotFoundException($"Expected field '{expectedName}' but found '{entryName ?? "null"}' in JSON state.");

            SkipColon();
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
