using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class BinaryStateOutput : IStateOutput, IDisposable
    {
        private readonly MemoryStream _stream = new();
        private readonly BinaryWriter _rootWriter;
        private readonly Stack<IBinaryWriteScope> _scopes = new();
        private BinaryWriter _activeWriter;

        public BinaryStateOutput()
        {
            _rootWriter = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
            _activeWriter = _rootWriter;
        }

        public ReadOnlyMemory<byte> GetBuffer() => _stream.ToArray().AsMemory();

        public void BeginArrayProperty(string name)
        {
            if (name != "NetworkStates")
                throw new NotSupportedException($"Binary array property '{name}' is not supported.");

            NetworkStatesWriteScope networkStatesScope = new(Hash.StringToInt(name));
            _scopes.Push(networkStatesScope);
        }

        public void BeginArrayElement() { }

        public void EndArray()
        {
            if (_scopes.Count == 0 || _scopes.Peek() is not NetworkStatesWriteScope networkStatesScope)
                throw new InvalidOperationException("EndArray called without matching BeginArrayProperty.");

            networkStatesScope.WriteTo(_activeWriter);
            _scopes.Pop();
        }

        public void BeginObjectProperty(string name)
        {
            if (!Guid.TryParse(name, out Guid identityId))
                throw new NotSupportedException($"Binary object property '{name}' is not supported.");

            if (_scopes.Peek() is not IdentityMapWriteScope parentIdentityMapScope)
                throw new InvalidOperationException("Identity payload must be written inside an identity map.");

            IdentityPayloadWriteScope identityScope = new(identityId, parentIdentityMapScope);
            _scopes.Push(identityScope);
            _activeWriter = identityScope.Writer;
        }

        public void BeginObjectElement()
        {
            if (_scopes.Count == 0)
            {
                _scopes.Push(new IdentityMapWriteScope());
                return;
            }

            if (_scopes.Peek() is not NetworkStatesWriteScope networkStatesScope)
                throw new InvalidOperationException("BeginObjectElement called outside of a network states array.");

            networkStatesScope.BeginState();
            _activeWriter = networkStatesScope.ActiveStateWriter;
        }

        public void EndObject()
        {
            if (_scopes.Count == 0)
                throw new InvalidOperationException("EndObject called without matching BeginObjectElement.");

            IBinaryWriteScope scope = _scopes.Peek();

            if (scope is NetworkStatesWriteScope networkStatesScope && networkStatesScope.HasActiveState)
            {
                networkStatesScope.EndState();
                _activeWriter = GetIdentityWriter();
                return;
            }

            if (scope is IdentityPayloadWriteScope identityPayloadScope)
            {
                identityPayloadScope.ParentMap.AddIdentity(identityPayloadScope.IdentityId, identityPayloadScope.ToArray());
                _scopes.Pop();
                _activeWriter = _rootWriter;
                return;
            }

            if (scope is IdentityMapWriteScope identityMapScope)
            {
                identityMapScope.WriteTo(_rootWriter);
                _scopes.Pop();
                _activeWriter = _rootWriter;
                return;
            }

            throw new InvalidOperationException("EndObject called in an invalid binary write scope.");
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
            WriteNameHash(name);
            _activeWriter.Write(value.x);
            _activeWriter.Write(value.y);
        }

        public void WriteVector3(string name, Vector3 value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value.x);
            _activeWriter.Write(value.y);
            _activeWriter.Write(value.z);
        }

        public void WriteVector2Int(string name, Vector2Int value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value.x);
            _activeWriter.Write(value.y);
        }

        public void WriteVector3Int(string name, Vector3Int value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value.x);
            _activeWriter.Write(value.y);
            _activeWriter.Write(value.z);
        }

        public void WriteQuaternion(string name, Quaternion value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value.x);
            _activeWriter.Write(value.y);
            _activeWriter.Write(value.z);
            _activeWriter.Write(value.w);
        }

        public void WriteGuid(string name, Guid value) => WriteEntry(name, value);

        public byte[] ToArray() => _stream.ToArray();

        public void Dispose()
        {
            _rootWriter.Dispose();
            _stream.Dispose();
        }

        private BinaryWriter GetIdentityWriter()
        {
            foreach (IBinaryWriteScope scope in _scopes)
            {
                if (scope is IdentityPayloadWriteScope identityPayloadScope)
                    return identityPayloadScope.Writer;
            }

            return _rootWriter;
        }

        private void WriteEntry(string name, byte value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value);
        }

        private void WriteEntry(string name, int value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value);
        }

        private void WriteEntry(string name, long value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value);
        }

        private void WriteEntry(string name, float value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value);
        }

        private void WriteEntry(string name, double value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value);
        }

        private void WriteEntry(string name, bool value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value);
        }

        private void WriteEntry(string name, string value)
        {
            WriteNameHash(name);
            byte[] stringBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            _activeWriter.Write((ushort)stringBytes.Length);
            _activeWriter.Write(stringBytes);
        }

        private void WriteEntry(string name, Guid value)
        {
            WriteNameHash(name);
            _activeWriter.Write(value.ToByteArray());
        }

        private void WriteNameHash(string name)
        {
            _activeWriter.Write(Hash.StringToInt(name));
        }

        private interface IBinaryWriteScope { }

        private sealed class IdentityMapWriteScope : IBinaryWriteScope
        {
            private readonly List<IdentityEntry> _entries = new();

            public void AddIdentity(Guid identityId, byte[] payload)
            {
                _entries.Add(new IdentityEntry(identityId, payload));
            }

            public void WriteTo(BinaryWriter writer)
            {
                writer.Write(_entries.Count);
                foreach (IdentityEntry entry in _entries)
                {
                    writer.Write(entry.IdentityId.ToByteArray());
                    writer.Write(entry.Payload.Length);
                    writer.Write(entry.Payload);
                }
            }

            private readonly struct IdentityEntry
            {
                public IdentityEntry(Guid identityId, byte[] payload)
                {
                    IdentityId = identityId;
                    Payload = payload;
                }

                public Guid IdentityId { get; }
                public byte[] Payload { get; }
            }
        }

        private sealed class IdentityPayloadWriteScope : IBinaryWriteScope
        {
            private readonly MemoryStream _stream = new();
            private readonly BinaryWriter _writer;

            public IdentityPayloadWriteScope(Guid identityId, IdentityMapWriteScope parentMap)
            {
                IdentityId = identityId;
                ParentMap = parentMap;
                _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
            }

            public Guid IdentityId { get; }
            public IdentityMapWriteScope ParentMap { get; }
            public BinaryWriter Writer => _writer;

            public byte[] ToArray() => _stream.ToArray();
        }

        private sealed class NetworkStatesWriteScope : IBinaryWriteScope
        {
            private readonly int _propertyHash;
            private readonly List<byte[]> _statePayloads = new();
            private MemoryStream _activeStateStream;
            private BinaryWriter _activeStateWriter;

            public NetworkStatesWriteScope(int propertyHash)
            {
                _propertyHash = propertyHash;
            }

            public bool HasActiveState => _activeStateStream != null;

            public BinaryWriter ActiveStateWriter => _activeStateWriter;

            public void BeginState()
            {
                _activeStateStream = new MemoryStream();
                _activeStateWriter = new BinaryWriter(_activeStateStream, Encoding.UTF8, leaveOpen: true);
            }

            public void EndState()
            {
                _activeStateWriter.Dispose();
                _statePayloads.Add(_activeStateStream.ToArray());
                _activeStateStream.Dispose();
                _activeStateStream = null;
                _activeStateWriter = null;
            }

            public void WriteTo(BinaryWriter writer)
            {
                writer.Write(_propertyHash);
                writer.Write(_statePayloads.Count);
                foreach (byte[] statePayload in _statePayloads)
                {
                    writer.Write(statePayload.Length);
                    writer.Write(statePayload);
                }
            }
        }
    }
}
