using System;

namespace BananaParty.WebSocketRelay
{
    public interface ISocket : IDisposable
    {
        bool IsConnected { get; }

        /// <summary>
        /// True once the connection attempt failed or the open connection ended.
        /// </summary>
        bool IsClosed { get; }

        /// <summary>
        /// Why the socket closed, for diagnostics. Null until the socket closes.
        /// </summary>
        string CloseReason { get; }

        /// <summary>
        /// Bytes passed to <see cref="Send"/> that have not been handed to the network yet.
        /// Keeps growing while the connection cannot keep up.
        /// </summary>
        int PendingSendBytes { get; }

        bool HasUnreadPayloadQueue { get; }

        byte[] ReadPayloadQueue();

        /// <summary>
        /// Operation is not immediate. Check <see cref="IsConnected"/> for connection status.
        /// </summary>
        void Connect();

        void Send(byte[] payloadBytes);

        /// <summary>
        /// Operation is not immediate. Check <see cref="IsConnected"/> for connection status.
        /// </summary>
        void Disconnect();
    }
}
