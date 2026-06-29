namespace BananaParty.WebSocketRelay
{
    public static class RelayMessageType
    {
        public const byte JoinRoom = 0x01;
        public const byte LeaveRoom = 0x02;
        public const byte SendMessage = 0x03;

        public const byte JoinedRoom = 0x10;
        public const byte LeftRoom = 0x11;
        public const byte RoomMessage = 0x12;
    }
}
