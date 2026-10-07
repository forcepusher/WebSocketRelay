namespace BananaParty.WebSocketRelay
{
    public static class NetworkMessage
    {
        public const byte SyncIdentities = 1;
        public const byte Rpc = 2;
        public const byte ReliableRpc = 3;
        public const byte RpcAcknowledgement = 4;
    }
}
