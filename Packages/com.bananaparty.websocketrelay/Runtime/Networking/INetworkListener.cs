using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkListener : IRelayListener
    {
        void OnPlayerAdded(NetworkPlayer networkPlayer);
        void OnPlayerRemoved(NetworkPlayer networkPlayer);
    }
}
