using System;
using System.Collections.Generic;
using System.Linq;
using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay.Tests
{
    /// <summary>
    /// <see cref="ISocket"/> that tests open, close, and feed by hand.
    /// </summary>
    public class FakeSocket : ISocket
    {
        private readonly Queue<byte[]> _incomingPayloads = new();

        public bool IsConnected { get; private set; }

        public bool IsClosed { get; private set; }

        public string CloseReason { get; private set; }

        public int PendingSendBytes { get; set; }

        public bool HasUnreadPayloadQueue => _incomingPayloads.Count > 0;

        public bool IsConnectCalled { get; private set; }

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Answers pings with pongs right away, like a relay server on a perfect connection.
        /// </summary>
        public bool AnswersPings { get; set; } = true;

        /// <summary>
        /// Closes the socket on the next send, like a connection dropping between polls.
        /// </summary>
        public bool CloseOnNextSend { get; set; }

        public List<byte[]> SentMessages { get; } = new();

        public byte[] ReadPayloadQueue() => _incomingPayloads.Dequeue();

        public void Connect()
        {
            if (IsConnectCalled)
                throw new InvalidOperationException($"{nameof(FakeSocket)} can only connect once.");

            IsConnectCalled = true;
        }

        public void Send(byte[] payloadBytes)
        {
            if (CloseOnNextSend)
            {
                CloseOnNextSend = false;
                Close("Closed while sending");
            }

            if (!IsConnected)
                throw new InvalidOperationException($"Trying to use {nameof(Send)} while not {nameof(IsConnected)}.");

            SentMessages.Add(payloadBytes);

            if (AnswersPings && payloadBytes.Length > 0 && payloadBytes[0] == RelayMessageType.Ping)
                Receive(CreatePong(payloadBytes));
        }

        public void Disconnect() => Close("Disconnected locally");

        public void Dispose()
        {
            IsDisposed = true;
            Close("Disposed");
        }

        public void Open()
        {
            if (IsClosed)
                throw new InvalidOperationException("Closed sockets cannot open.");

            IsConnected = true;
        }

        public void Close(string reason = "Closed by test")
        {
            if (IsClosed)
                return;

            IsConnected = false;
            IsClosed = true;
            CloseReason = reason;
        }

        public void Receive(byte[] payloadBytes) => _incomingPayloads.Enqueue(payloadBytes);

        public void ReceiveChannelMessage(Guid senderGuid, string channel, byte[] data)
            => Receive(RelayMessageCodec.CreateChannelMessage(senderGuid, channel, data));

        public static byte[] CreatePong(byte[] pingBytes)
        {
            byte[] pongBytes = (byte[])pingBytes.Clone();
            pongBytes[0] = RelayMessageType.Pong;
            return pongBytes;
        }

        public IEnumerable<byte[]> SentOfType(byte messageType)
            => SentMessages.Where(message => message.Length > 0 && message[0] == messageType);

        public List<string> SentSubscriptions()
            => SentOfType(RelayMessageType.Subscribe).Select(message => RelayMessageCodec.ReadChannel(message)).ToList();

        public List<string> SentUnsubscriptions()
            => SentOfType(RelayMessageType.Unsubscribe).Select(message => RelayMessageCodec.ReadChannel(message)).ToList();
    }

    public class FakeSocketFactory
    {
        public List<FakeSocket> Sockets { get; } = new();

        public FakeSocket Latest => Sockets.Count > 0 ? Sockets[Sockets.Count - 1] : null;

        /// <summary>
        /// Runs on every created socket, e.g. to open it right away.
        /// </summary>
        public Action<FakeSocket> OnCreate { get; set; }

        public Exception ExceptionToThrow { get; set; }

        public ISocket Create()
        {
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;

            FakeSocket socket = new();
            Sockets.Add(socket);
            OnCreate?.Invoke(socket);
            return socket;
        }
    }

    public class ManualClock
    {
        // Not zero, so code mistaking zero for "never happened" fails.
        public double Now { get; private set; } = 1000d;

        public void Advance(double seconds) => Now += seconds;

        public double GetTime() => Now;
    }
}
