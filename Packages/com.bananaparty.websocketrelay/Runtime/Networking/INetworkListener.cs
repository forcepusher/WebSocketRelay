namespace BananaParty.WebSocketRelay
{
    public interface INetworkListener
    {
        void OnConnectedToRelay();

        void OnConnectedToRoom(Room room);
        void OnDisconnectedFromRoom(Room room);

        void OnRoomPlayerAdded(NetworkPlayer networkPlayer);
        void OnRoomPlayerRemoved(NetworkPlayer networkPlayer);
    }
}
