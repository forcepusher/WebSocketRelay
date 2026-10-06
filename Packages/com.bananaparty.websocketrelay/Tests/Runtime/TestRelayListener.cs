using System;
using System.Collections.Generic;
using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay.Tests
{
    public class TestRelayListener : IRelayListener
    {
        public event Action Disconnected;
        public event Action<RelayConnectionState, RelayConnectionState, string> ConnectionStateChanged;
        public event Action<Guid, string, byte[]> ChannelMessageReceived;

        public List<RelayConnectionState> States { get; } = new();

        public string LastReason { get; private set; }

        public void OnConnectionStateChanged(RelayConnectionState previousState, RelayConnectionState state, string reason)
        {
            States.Add(state);
            LastReason = reason;
            ConnectionStateChanged?.Invoke(previousState, state, reason);

            if (state == RelayConnectionState.Disconnected)
                Disconnected?.Invoke();
        }

        public void OnChannelMessage(Guid senderGuid, string channel, byte[] data)
            => ChannelMessageReceived?.Invoke(senderGuid, channel, data);
    }
}
