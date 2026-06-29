namespace BananaParty.WebSocketRelay
{
    public static class RelayMessageHeader
    {
        public const int Length = 5;

        public const int RoomIdOffset = 1;

        public const int PayloadOffset = 5;
    }
}
