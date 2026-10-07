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
        private readonly StringBuilder _stringBuilder = new();

        // One entry per open object or array, innermost on top.
        private readonly Stack<Scope> _scopes = new();

        private bool _hasStarted;

        public JsonStateOutput(bool prettyPrint = true, bool bracesOnNewLine = true, int spaceIndentationCount = 4)
        {
            _prettyPrint = prettyPrint;
            _bracesOnNewLine = bracesOnNewLine;
            _indentationCount = spaceIndentationCount;
        }

        public void WriteByte(string name, byte value) => WriteValue(name, value.ToString(CultureInfo.InvariantCulture));

        public void WriteInt(string name, int value) => WriteValue(name, value.ToString(CultureInfo.InvariantCulture));

        public void WriteLong(string name, long value) => WriteValue(name, value.ToString(CultureInfo.InvariantCulture));

        public void WriteFloat(string name, float value) => WriteValue(name, FormatFloat(value));

        public void WriteDouble(string name, double value) => WriteValue(name, value.ToString("R", CultureInfo.InvariantCulture));

        public void WriteBool(string name, bool value) => WriteValue(name, value ? "true" : "false");

        public void WriteString(string name, string value)
        {
            BeginEntry(name);
            AppendQuoted(value ?? string.Empty);
        }

        public void WriteGuid(string name, Guid value) => WriteString(name, value.ToString());

        public void WriteVector2(string name, Vector2 value) =>
            WriteValue(name, $"{{\"x\":{FormatFloat(value.x)},\"y\":{FormatFloat(value.y)}}}");

        public void WriteVector3(string name, Vector3 value) =>
            WriteValue(name, $"{{\"x\":{FormatFloat(value.x)},\"y\":{FormatFloat(value.y)},\"z\":{FormatFloat(value.z)}}}");

        public void WriteVector2Int(string name, Vector2Int value) =>
            WriteValue(name, $"{{\"x\":{value.x.ToString(CultureInfo.InvariantCulture)},\"y\":{value.y.ToString(CultureInfo.InvariantCulture)}}}");

        public void WriteVector3Int(string name, Vector3Int value) =>
            WriteValue(name, $"{{\"x\":{value.x.ToString(CultureInfo.InvariantCulture)},\"y\":{value.y.ToString(CultureInfo.InvariantCulture)},\"z\":{value.z.ToString(CultureInfo.InvariantCulture)}}}");

        public void WriteQuaternion(string name, Quaternion value) =>
            WriteValue(name, $"{{\"x\":{FormatFloat(value.x)},\"y\":{FormatFloat(value.y)},\"z\":{FormatFloat(value.z)},\"w\":{FormatFloat(value.w)}}}");

        public void WriteColor(string name, Color value) =>
            WriteValue(name, $"{{\"r\":{FormatFloat(value.r)},\"g\":{FormatFloat(value.g)},\"b\":{FormatFloat(value.b)},\"a\":{FormatFloat(value.a)}}}");

        public void BeginArrayProperty(string name) => BeginProperty(name, '[', ']');

        public void BeginObjectProperty(string name) => BeginProperty(name, '{', '}');

        /// <summary>
        /// Starts the root object on first use, otherwise an object inside the current array.
        /// </summary>
        public void BeginObjectElement()
        {
            if (!_hasStarted)
            {
                StartRoot();
                return;
            }

            WriteItemSeparator();
            OpenScope('{', '}');
        }

        public void EndArray() => CloseScope(']');

        public void EndObject() => CloseScope('}');

        /// <summary>
        /// The JSON written so far, with every scope that is still open closed.
        /// </summary>
        public override string ToString()
        {
            if (!_hasStarted)
                return "{}";

            StringBuilder result = new(_stringBuilder.ToString());
            int depth = _scopes.Count;
            foreach (Scope scope in _scopes)
            {
                depth--;
                if (_prettyPrint && scope.HasItems)
                    AppendLineBreak(result, depth);

                result.Append(scope.Closer);
            }

            return result.ToString();
        }

        // Shortest text that parses back to the same value, which plain ToString does not guarantee on every runtime.
        private static string FormatFloat(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private void BeginProperty(string name, char opener, char closer)
        {
            BeginEntry(name);
            if (_prettyPrint && _bracesOnNewLine)
                AppendLineBreak(_stringBuilder, _scopes.Count);

            OpenScope(opener, closer);
        }

        private void WriteValue(string name, string serializedValue)
        {
            BeginEntry(name);
            _stringBuilder.Append(serializedValue);
        }

        private void BeginEntry(string name)
        {
            if (!_hasStarted)
                StartRoot();

            WriteItemSeparator();
            AppendQuoted(name);
            _stringBuilder.Append(':');
        }

        private void StartRoot()
        {
            _hasStarted = true;
            OpenScope('{', '}');
        }

        private void OpenScope(char opener, char closer)
        {
            _stringBuilder.Append(opener);
            _scopes.Push(new Scope(closer));
        }

        private void CloseScope(char closer)
        {
            if (_scopes.Count == 0 || _scopes.Peek().Closer != closer)
                return;

            Scope scope = _scopes.Pop();
            if (_prettyPrint && scope.HasItems)
                AppendLineBreak(_stringBuilder, _scopes.Count);

            _stringBuilder.Append(closer);
        }

        private void WriteItemSeparator()
        {
            Scope scope = _scopes.Pop();
            if (scope.HasItems)
                _stringBuilder.Append(',');

            if (_prettyPrint)
                AppendLineBreak(_stringBuilder, _scopes.Count + 1);

            scope.HasItems = true;
            _scopes.Push(scope);
        }

        private void AppendLineBreak(StringBuilder stringBuilder, int depth)
        {
            stringBuilder.Append('\n');
            stringBuilder.Append(' ', depth * _indentationCount);
        }

        private void AppendQuoted(string value)
        {
            _stringBuilder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        _stringBuilder.Append("\\\"");
                        break;
                    case '\\':
                        _stringBuilder.Append("\\\\");
                        break;
                    case '\n':
                        _stringBuilder.Append("\\n");
                        break;
                    case '\r':
                        _stringBuilder.Append("\\r");
                        break;
                    case '\t':
                        _stringBuilder.Append("\\t");
                        break;
                    case < ' ':
                        _stringBuilder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        break;
                    default:
                        _stringBuilder.Append(character);
                        break;
                }
            }

            _stringBuilder.Append('"');
        }

        private struct Scope
        {
            public Scope(char closer)
            {
                Closer = closer;
                HasItems = false;
            }

            public char Closer { get; }

            public bool HasItems { get; set; }
        }
    }
}
