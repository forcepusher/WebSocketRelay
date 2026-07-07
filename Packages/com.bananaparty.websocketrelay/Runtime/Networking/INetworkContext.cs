using System;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkContext
    {
        Guid LocalClientIdentity { get; set; }

        void RegisterNetworkIdentity(INetworkIdentity networkIdentity);

        void UnregisterNetworkIdentity(INetworkIdentity networkIdentity);

        void ReadNetworkStates(IStateInput stateInput);

        void WriteNetworkStates(IStateOutput stateOutput);
    }
}
