using System;
using System.Collections.Generic;
using System.IO;

namespace BananaParty.WebSocketRelay.Transport
{
    public class RelayClient : IDisposable
    {
        private readonly Socket _socket;
        private readonly HashSet<string> _subscribedTopics = new();

        public bool IsConnected => _socket.IsConnected;

        public bool HasUnreadPayloadQueue => _socket.HasUnreadPayloadQueue;

        public Guid ClientGuid { get; }

        public HashSet<string> SubscribedTopics => _subscribedTopics;

        private IRelayListener _relayListener;
        private bool _wasConnected;

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
        /// Drains queued WebSocket frames and dispatches topic messages.
        /// Call this periodically (e.g. in Update).
        /// </summary>
        public void ProcessIncomingMessages()
        {
            while (_socket.IsConnected && _socket.HasUnreadPayloadQueue)
            {
                byte[] payload = _socket.ReadPayloadQueue();
                ProcessPayload(payload);
            }

            UpdateConnectionState();
        }

        public void SubscribeToTopic(string topic)
        {
            if (!_subscribedTopics.Add(topic))
                return;

            _socket.Send(RelayMessageCodec.CreateProtocolMessage(RelayMessageType.Subscribe, topic));
        }

        public void UnsubscribeToTopic(string topic)
        {
            if (!_subscribedTopics.Remove(topic))
                throw new KeyNotFoundException($"Not subscribed to topic '{topic}'.");

            _socket.Send(RelayMessageCodec.CreateProtocolMessage(RelayMessageType.Unsubscribe, topic));
        }

        public void Send(string topic, byte[] data)
        {
            if (!_subscribedTopics.Contains(topic))
                throw new KeyNotFoundException($"Not subscribed to topic '{topic}'.");

            _socket.Send(RelayMessageCodec.CreateTopicMessage(ClientGuid, topic, data));
        }

        public void Dispose()
        {
            _subscribedTopics.Clear();

            try
            {
                if (_socket.IsConnected)
                    _socket.Disconnect();
            }
            finally
            {
                NotifyDisconnectedIfNeeded();
                _socket.Dispose();
            }
        }

        private void UpdateConnectionState()
        {
            if (_socket.IsConnected)
                _wasConnected = true;
            else
                NotifyDisconnectedIfNeeded();
        }

        private void NotifyDisconnectedIfNeeded()
        {
            if (!_wasConnected && !_socket.IsConnected)
                return;

            _wasConnected = false;
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
                    case RelayMessageType.TopicMessage:
                        processedLength = ProcessTopicMessage(payloadBytes);
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

        private int ProcessTopicMessage(byte[] data)
        {
            int topicLength = RelayMessageCodec.ReadTopicLength(data, RelayMessageCodec.TopicMessageTopicLengthOffset);
            if (topicLength < 0)
                throw new InvalidDataException("Incomplete topic message.");

            Guid senderId = RelayMessageCodec.ReadGuid(data, RelayMessageCodec.TopicMessageGuidOffset);
            string topic = RelayMessageCodec.ReadTopic(data, RelayMessageCodec.TopicMessageTopicLengthOffset);
            int payloadOffset = RelayMessageCodec.GetTopicMessagePayloadOffset(topicLength);

            if (data.Length < payloadOffset)
                throw new InvalidDataException("Incomplete topic message.");

            if (!_subscribedTopics.Contains(topic))
                return data.Length;

            byte[] messageData = new byte[data.Length - payloadOffset];
            Array.Copy(data, payloadOffset, messageData, 0, messageData.Length);
            _relayListener.OnTopicMessage(senderId, topic, messageData);

            return data.Length;
        }

        private static int SkipUnknownMessage(byte[] data)
        {
            return data.Length;
        }
    }
}
