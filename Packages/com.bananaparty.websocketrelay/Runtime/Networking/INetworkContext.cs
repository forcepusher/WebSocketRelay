namespace BananaParty.WebSocketRelay
{
    public interface INetworkContext
    {
        void RegisterNetworkIdentity(INetworkIdentity networkIdentity);

        void UnregisterNetworkIdentity(INetworkIdentity networkIdentity);

        void WriteStates(IStateOutput stateOutput);
    }
}
