using System;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkContext
    {
        Guid LocalClientIdentity { get; set; }

        void RegisterNetworkIdentity(INetworkIdentity networkIdentity);

        void UnregisterNetworkIdentity(INetworkIdentity networkIdentity);

        void ReadStates(IStateInput stateInput);

        void WriteStates(IStateOutput stateOutput);
    }
}
