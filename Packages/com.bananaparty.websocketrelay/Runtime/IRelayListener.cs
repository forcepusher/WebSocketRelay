using System;

namespace BananaParty.WebSocketRelay
{
    public interface IRelayListener
    {
        void ProcessRelayMessage(Guid senderGuid, string topic, byte[] data);
    }
}
