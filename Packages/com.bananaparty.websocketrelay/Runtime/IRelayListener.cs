using System;

namespace BananaParty.WebSocketRelay
{
    public interface IRelayListener
    {
        void ProcessConnected(Guid clientGuid);

        void ProcessSubscribed(string topic);

        void ProcessUnsubscribed(string topic);

        void ProcessTopicMessage(Guid senderGuid, string topic, byte[] data);
    }
}
