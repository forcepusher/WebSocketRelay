namespace BananaParty.WebSocketRelay.Transport
{
    public static class RelayMessageType
    {
        public const byte Subscribe = 0x01;
        public const byte Unsubscribe = 0x02;

        public const byte Subscribed = 0x10;
        public const byte Unsubscribed = 0x11;
        public const byte TopicMessage = 0x12;
    }
}
