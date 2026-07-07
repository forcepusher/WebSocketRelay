using System;
using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay.Tests
{
    public class TestRelayListener : IRelayListener
    {
        public event Action<Guid> Connected;
        public event Action<string> Subscribed;
        public event Action<string> Unsubscribed;
        public event Action<Guid, string, byte[]> TopicMessageReceived;

        public void OnConnectedToRelay(Guid clientGuid)
            => Connected?.Invoke(clientGuid);

        public void OnSubscribedToTopic(string topic)
            => Subscribed?.Invoke(topic);

        public void OnUnsubscribedFromTopic(string topic)
            => Unsubscribed?.Invoke(topic);

        public void OnTopicMessage(Guid senderGuid, string topic, byte[] data)
            => TopicMessageReceived?.Invoke(senderGuid, topic, data);
    }
}
