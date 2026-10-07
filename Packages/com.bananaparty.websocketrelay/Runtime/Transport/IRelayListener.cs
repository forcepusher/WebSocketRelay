using System;

namespace BananaParty.WebSocketRelay.Transport
{
    public interface IRelayListener
    {
        /// <summary>
        /// Called on every <see cref="RelayClient.State"/> change, except when the client is disposed.
        /// </summary>
        /// <param name="reason">Why the connection ended, or null when it was established.</param>
        void OnConnectionStateChanged(RelayConnectionState previousState, RelayConnectionState state, string reason);

        void OnChannelMessage(Guid senderGuid, string channel, byte[] data);
    }
}
