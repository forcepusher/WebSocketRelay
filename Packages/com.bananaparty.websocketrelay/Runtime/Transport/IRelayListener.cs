using System;

namespace BananaParty.WebSocketRelay.Transport
{
    public interface IRelayListener
    {
        void OnConnectedToRelay();

        void OnDisconnectedFromRelay();

        void OnTopicMessage(Guid senderGuid, string topic, byte[] data);
    }
}
