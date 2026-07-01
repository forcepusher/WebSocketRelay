using System;

namespace BananaParty.WebSocketRelay.Tests
{
    public class TestRelayListener : IRelayListener
    {
        public event Action<Guid> Connected;
        public event Action<string> Subscribed;
        public event Action<string> Unsubscribed;
        public event Action<Guid, string, byte[]> TopicMessageReceived;

        public void ProcessConnected(Guid clientGuid)
            => Connected?.Invoke(clientGuid);

        public void ProcessSubscribed(string topic)
            => Subscribed?.Invoke(topic);

        public void ProcessUnsubscribed(string topic)
            => Unsubscribed?.Invoke(topic);

        public void ProcessTopicMessage(Guid senderGuid, string topic, byte[] data)
            => TopicMessageReceived?.Invoke(senderGuid, topic, data);
    }
}
