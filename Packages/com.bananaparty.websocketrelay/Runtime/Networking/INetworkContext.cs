namespace BananaParty.WebSocketRelay
{
    public interface INetworkContext
    {
        void RegisterNetworkIdentity(INetworkIdentity networkIdentity);

        void UnregisterNetworkIdentity(INetworkIdentity networkIdentity);

        void ReadStates(IStateInput stateInput);

        void WriteStates(IStateOutput stateOutput);
    }
}
