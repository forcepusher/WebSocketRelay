using System;

namespace BananaParty.WebSocketRelay
{
    public interface IRelayListener
    {
        void OnConnectedToRelay(Guid clientGuid);

        void OnSubscribedToTopic(string topic);

        void OnUnsubscribedFtomTopic(string topic);

        void OnTopicMessage(Guid senderGuid, string topic, byte[] data);
    }
}
