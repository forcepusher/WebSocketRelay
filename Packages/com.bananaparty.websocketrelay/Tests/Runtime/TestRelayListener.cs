using System;
using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay.Tests
{
    public class TestRelayListener : IRelayListener
    {
        public event Action Connected;
        public event Action Disconnected;
        public event Action<Guid, string, byte[]> TopicMessageReceived;

        public void OnConnectedToRelay()
            => Connected?.Invoke();

        public void OnDisconnectedFromRelay()
            => Disconnected?.Invoke();

        public void OnTopicMessage(Guid senderGuid, string topic, byte[] data)
            => TopicMessageReceived?.Invoke(senderGuid, topic, data);
    }
}
