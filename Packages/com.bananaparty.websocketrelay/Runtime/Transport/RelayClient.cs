using System;
using System.Collections.Generic;
using System.IO;

namespace BananaParty.WebSocketRelay.Transport
{
    public class RelayClient : IDisposable
    {
        private enum ConnectionState
        {
            Idle,
            Connected,
            Disconnected
        }

        private readonly Socket _socket;
        private readonly HashSet<string> _subscribedChannels = new();

        public bool IsConnected => _connectionState == ConnectionState.Connected;

        public bool HasUnreadPayloadQueue => _socket.HasUnreadPayloadQueue;

        public Guid ClientGuid { get; }

        public HashSet<string> SubscribedChannels => _subscribedChannels;

        private IRelayListener _relayListener;
        private ConnectionState _connectionState = ConnectionState.Idle;

        public RelayClient(string serverAddress, IRelayListener relayListener, Guid clientGuid)
        {
            ClientGuid = clientGuid;
            _socket = new Socket(serverAddress);
            _relayListener = relayListener;
        }

        public void Connect()
        {
            _socket.Connect();
        }

        /// <summary>
        /// Drains queued WebSocket frames, dispatches channel messages, and updates connection state.
        /// Call this periodically (e.g. in Update).
        /// </summary>
        public void ProcessIncomingMessages()
        {
            UpdateConnectionState();

            while (_socket.IsConnected && _socket.HasUnreadPayloadQueue)
            {
                byte[] payload = _socket.ReadPayloadQueue();
                ProcessPayload(payload);
            }

            UpdateConnectionState();
        }

        public void SubscribeToChannel(string channel)
        {
            if (!_subscribedChannels.Add(channel))
                return;

            _socket.Send(RelayMessageCodec.CreateProtocolMessage(RelayMessageType.Subscribe, channel));
        }

        public void UnsubscribeFromChannel(string channel)
        {
            if (!_subscribedChannels.Remove(channel))
                throw new KeyNotFoundException($"Not subscribed to channel '{channel}'.");

            _socket.Send(RelayMessageCodec.CreateProtocolMessage(RelayMessageType.Unsubscribe, channel));
        }

        public void Send(string channel, byte[] data)
        {
            if (!_subscribedChannels.Contains(channel))
                throw new KeyNotFoundException($"Not subscribed to channel '{channel}'.");

            _socket.Send(RelayMessageCodec.CreateChannelMessage(ClientGuid, channel, data));
        }

        public void Dispose()
        {
            _subscribedChannels.Clear();

            try
            {
                if (_socket.IsConnected)
                    _socket.Disconnect();
            }
            finally
            {
                _socket.Dispose();
            }
        }

        private void UpdateConnectionState()
        {
            if (_socket.IsConnected)
            {
                if (_connectionState == ConnectionState.Idle)
                    _connectionState = ConnectionState.Connected;

                return;
            }

            if (_connectionState != ConnectionState.Connected)
                return;

            _connectionState = ConnectionState.Disconnected;
            _relayListener.OnDisconnectedFromRelay();
        }

        internal void ProcessPayload(byte[] payloadBytes)
        {
            while (payloadBytes.Length > 0)
            {
                int processedLength;
                byte type = payloadBytes[0];

                switch (type)
                {
                    case RelayMessageType.ChannelMessage:
                        processedLength = ProcessChannelMessage(payloadBytes);
                        break;
                    default:
                        processedLength = SkipUnknownMessage(payloadBytes);
                        break;
                }

                if (processedLength >= payloadBytes.Length)
                    return;

                byte[] remaining = new byte[payloadBytes.Length - processedLength];
                Array.Copy(payloadBytes, processedLength, remaining, 0, remaining.Length);
                payloadBytes = remaining;
            }
        }

        private int ProcessChannelMessage(byte[] data)
        {
            int channelLength = RelayMessageCodec.ReadChannelLength(data, RelayMessageCodec.ChannelMessageChannelLengthOffset);
            if (channelLength < 0)
                throw new InvalidDataException("Incomplete channel message.");

            Guid senderId = RelayMessageCodec.ReadGuid(data, RelayMessageCodec.ChannelMessageGuidOffset);
            string channel = RelayMessageCodec.ReadChannel(data, RelayMessageCodec.ChannelMessageChannelLengthOffset);
            int payloadOffset = RelayMessageCodec.GetChannelMessagePayloadOffset(channelLength);

            if (data.Length < payloadOffset)
                throw new InvalidDataException("Incomplete channel message.");

            if (!_subscribedChannels.Contains(channel))
                return data.Length;

            byte[] messageData = new byte[data.Length - payloadOffset];
            Array.Copy(data, payloadOffset, messageData, 0, messageData.Length);
            _relayListener.OnChannelMessage(senderId, channel, messageData);

            return data.Length;
        }

        private static int SkipUnknownMessage(byte[] data)
        {
            return data.Length;
        }
    }
}
