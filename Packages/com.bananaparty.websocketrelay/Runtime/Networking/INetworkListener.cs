using System;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkListener
    {
        void OnConnectedToRelay(Guid clientGuid);

        void OnConnectedToRoom(Room room);
        void OnDisconnectedFromRoom(Room room);

        void OnRoomPlayerAdded(NetworkPlayer networkPlayer);
        void OnRoomPlayerRemoved(NetworkPlayer networkPlayer);
    }
}
