using System;

namespace BananaParty.WebSocketRelay.Transport
{
    public interface IRelayListener
    {
        void OnConnectedToRelay();

        void OnSubscribedToTopic(string topic);

        void OnUnsubscribedFromTopic(string topic);

        void OnTopicMessage(Guid senderGuid, string topic, byte[] data);
    }
}
