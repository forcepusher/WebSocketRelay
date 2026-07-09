using System;

namespace BananaParty.WebSocketRelay.Transport
{
    public interface IRelayListener
    {
        void OnConnectedToRelay();

        void OnTopicMessage(Guid senderGuid, string topic, byte[] data);
    }
}
