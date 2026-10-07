namespace BananaParty.WebSocketRelay.Transport
{
    public static class RelayMessageType
    {
        public const byte Subscribe = 0x01;
        public const byte Unsubscribe = 0x02;
        public const byte ChannelMessage = 0x03;
        public const byte Ping = 0x04;
        public const byte Pong = 0x05;
        public const byte Hello = 0x06;
        public const byte ChannelState = 0x07;
    }
}
