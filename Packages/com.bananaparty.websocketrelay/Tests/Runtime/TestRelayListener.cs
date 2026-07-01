using System;

namespace BananaParty.WebSocketRelay.Tests
{
    public class TestRelayListener : IRelayListener
    {
        public event Action<Guid, string, byte[]> MessageReceived;

        public void ProcessRelayMessage(Guid senderGuid, string topic, byte[] data)
            => MessageReceived?.Invoke(senderGuid, topic, data);
    }
}
