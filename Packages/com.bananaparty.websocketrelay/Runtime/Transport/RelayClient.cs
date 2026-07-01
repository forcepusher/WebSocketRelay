using System;
using System.Collections.Generic;
using System.IO;

namespace BananaParty.WebSocketRelay.Transport
{
    public class RelayClient : IDisposable
    {
        private readonly Socket _socket;
        private readonly HashSet<string> _subscriptions = new();

        public bool IsConnected => _socket.IsConnected;

        public bool HasUnreadPayloadQueue => _socket.HasUnreadPayloadQueue;

        public Guid ClientId { get; private set; }

        private IRelayListener _relayListener;

        public RelayClient(string serverAddress, IRelayListener relayListener)
        {
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
            while (_socket.HasUnreadPayloadQueue)
            {
                byte[] payload = _socket.ReadPayloadQueue();
                ProcessPayload(payload);
            }
        }

        public void Subscribe(string topic)
        {
            if (!_subscriptions.Add(topic))
                return;

            _socket.Send(RelayMessageCodec.CreateMessage(RelayMessageType.Subscribe, topic));
        }

        public void Unsubscribe(string topic)
        {
            if (!_subscriptions.Remove(topic))
                throw new KeyNotFoundException($"Not subscribed to topic '{topic}'.");

            _socket.Send(RelayMessageCodec.CreateMessage(RelayMessageType.Unsubscribe, topic));
        }

        public void Send(string topic, byte[] data)
        {
            if (!_subscriptions.Contains(topic))
                throw new KeyNotFoundException($"Not subscribed to topic '{topic}'.");

            _socket.Send(RelayMessageCodec.CreateMessage(RelayMessageType.Send, topic, data));
        }

        public void Dispose()
        {
            _subscriptions.Clear();

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

        internal void ProcessPayload(byte[] payloadBytes)
        {
            while (payloadBytes.Length > 0)
            {
                int processedLength;
                byte type = payloadBytes[0];

                switch (type)
                {
                    case RelayMessageType.Connected:
                        processedLength = ProcessConnectedMessage(payloadBytes);
                        break;
                    case RelayMessageType.Subscribed:
                        processedLength = ProcessSubscribedMessage(payloadBytes);
                        break;
                    case RelayMessageType.Unsubscribed:
                        processedLength = ProcessUnsubscribedMessage(payloadBytes);
                        break;
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

        private int ProcessConnectedMessage(byte[] data)
        {
            if (data.Length < RelayMessageCodec.ConnectedMessageSize)
                throw new InvalidDataException("Incomplete connected message.");

            ClientId = RelayMessageCodec.ReadGuid(data);
            _relayListener.OnConnectedToRelay(ClientId);
            return RelayMessageCodec.ConnectedMessageSize;
        }

        private int ProcessSubscribedMessage(byte[] data)
        {
            int topicLength = RelayMessageCodec.ReadTopicLength(data);
            if (topicLength < 0)
                throw new InvalidDataException("Incomplete topic control message.");

            _relayListener.OnSubscribedToTopic(RelayMessageCodec.ReadTopic(data));
            return RelayMessageCodec.GetPayloadOffset(topicLength);
        }

        private int ProcessUnsubscribedMessage(byte[] data)
        {
            int topicLength = RelayMessageCodec.ReadTopicLength(data);
            if (topicLength < 0)
                throw new InvalidDataException("Incomplete topic control message.");

            _relayListener.OnUnsubscribedFtomTopic(RelayMessageCodec.ReadTopic(data));
            return RelayMessageCodec.GetPayloadOffset(topicLength);
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

            if (!_subscriptions.Contains(topic))
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
