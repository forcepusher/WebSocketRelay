using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class JsonStateOutput : IStateOutput
    {
        private readonly bool _prettyPrint;
        private readonly bool _bracesOnNewLine;
        private readonly int _indentationCount;
        private readonly StringBuilder _sb = new();
        private int _depth = 0;
        private bool _hasStarted = false;
        private readonly Stack<bool> _firstItemScopes = new();
        private readonly Stack<char> _closers = new();

        public JsonStateOutput(bool prettyPrint = true, bool bracesOnNewLine = true, int spaceIndentationCount = 4)
        {
            _prettyPrint = prettyPrint;
            _bracesOnNewLine = bracesOnNewLine;
            _indentationCount = spaceIndentationCount;
        }

        public void WriteByte(string name, byte value) => WriteEntry(name, value);

        public void WriteInt(string name, int value) => WriteEntry(name, value);

        public void WriteLong(string name, long value) => WriteEntry(name, value);

        public void WriteFloat(string name, float value) => WriteEntry(name, value);

        public void WriteDouble(string name, double value) => WriteEntry(name, value);

        public void WriteBool(string name, bool value) => WriteEntry(name, value);

        public void WriteString(string name, string value) => WriteEntry(name, value);

        public void WriteVector2(string name, Vector2 value)
        {
            string x = value.x.ToString(CultureInfo.InvariantCulture);
            string y = value.y.ToString(CultureInfo.InvariantCulture);
            WriteObjectEntry(name, $"{{\"x\":{x},\"y\":{y}}}");
        }

        public void WriteVector3(string name, Vector3 value)
        {
            string x = value.x.ToString(CultureInfo.InvariantCulture);
            string y = value.y.ToString(CultureInfo.InvariantCulture);
            string z = value.z.ToString(CultureInfo.InvariantCulture);
            WriteObjectEntry(name, $"{{\"x\":{x},\"y\":{y},\"z\":{z}}}");
        }

        public void WriteVector2Int(string name, Vector2Int value)
        {
            WriteObjectEntry(name, $"{{\"x\":{value.x},\"y\":{value.y}}}");
        }

        public void WriteVector3Int(string name, Vector3Int value)
        {
            WriteObjectEntry(name, $"{{\"x\":{value.x},\"y\":{value.y},\"z\":{value.z}}}");
        }

        public void WriteQuaternion(string name, Quaternion value)
        {
            string x = value.x.ToString(CultureInfo.InvariantCulture);
            string y = value.y.ToString(CultureInfo.InvariantCulture);
            string z = value.z.ToString(CultureInfo.InvariantCulture);
            string w = value.w.ToString(CultureInfo.InvariantCulture);
            WriteObjectEntry(name, $"{{\"x\":{x},\"y\":{y},\"z\":{z},\"w\":{w}}}");
        }

        public void WriteGuid(string name, Guid value) => WriteEntry(name, value.ToString());

        public override string ToString()
        {
            if (!_hasStarted) return "{}";

            StringBuilder result = new(_sb.ToString());
            int tempDepth = _depth;
            var closersCopy = new Stack<char>(_closers);

            while (closersCopy.Count > 0)
            {
                char closer = closersCopy.Pop();
                if (_prettyPrint && tempDepth > 1)
                {
                    result.Append('\n');
                    tempDepth--;
                    if (tempDepth > 0)
                        result.Append(new string(' ', tempDepth * _indentationCount));
                }
                else
                {
                    tempDepth--;
                }
                result.Append(closer);
            }
            return result.ToString();
        }

        private void WriteEntry(string name, byte value) => WritePrimitiveEntry(name, value.ToString(CultureInfo.InvariantCulture), false);

        private void WriteEntry(string name, int value) => WritePrimitiveEntry(name, value.ToString(CultureInfo.InvariantCulture), false);

        private void WriteEntry(string name, long value) => WritePrimitiveEntry(name, value.ToString(CultureInfo.InvariantCulture), false);

        private void WriteEntry(string name, float value) => WritePrimitiveEntry(name, value.ToString(CultureInfo.InvariantCulture), false);

        private void WriteEntry(string name, double value) => WritePrimitiveEntry(name, value.ToString(CultureInfo.InvariantCulture), false);

        private void WriteEntry(string name, bool value) => WritePrimitiveEntry(name, value ? "true" : "false", false);

        private void WriteEntry(string name, string value) => WritePrimitiveEntry(name, value ?? string.Empty, true);

        private void WriteObjectEntry(string name, string serializedObject)
        {
            EnsureStarted('{', '}');
            WriteItemSeparator();
            _sb.Append($"\"{name}\":{serializedObject}");
        }

        private void WritePrimitiveEntry(string name, string serializedValue, bool quoteValue)
        {
            EnsureStarted('{', '}');
            WriteItemSeparator();
            _sb.Append(quoteValue ? $"\"{name}\":\"{serializedValue}\"" : $"\"{name}\":{serializedValue}");
        }

        private void EnsureStarted(char open, char close)
        {
            if (_hasStarted) return;

            _sb.Append(open);
            _hasStarted = true;
            _depth++;
            _firstItemScopes.Push(true);
            _closers.Push(close);

            if (_prettyPrint)
            {
                _sb.Append('\n');
                AppendIndent();
            }
        }

        private void WriteItemSeparator()
        {
            bool isFirst = _firstItemScopes.Pop();
            if (!isFirst)
            {
                if (_prettyPrint)
                {
                    _sb.Append(",\n");
                    AppendIndent();
                }
                else
                {
                    _sb.Append(',');
                }
            }
            _firstItemScopes.Push(false);
        }

        private void AppendIndent()
        {
            _sb.Append(new string(' ', _depth * _indentationCount));
        }
    }
}
