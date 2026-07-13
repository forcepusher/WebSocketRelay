using System;
using System.Numerics;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkContext
    {
        Guid LocalClientIdentity { get; set; }

        AuthorityOrigin GetClosestAuthorityOrigin(Vector3 position);

        void RegisterNetworkIdentity(INetworkIdentity networkIdentity);

        void UnregisterNetworkIdentity(INetworkIdentity networkIdentity);

        void RegisterAuthorityOrigin(IAuthorityOrigin authorityOrigin);

        void UnregisterAuthorityOrigin(IAuthorityOrigin authorityOrigin);

        void ReadNetworkStates(IStateInput stateInput);

        void WriteNetworkStates(IStateOutput stateOutput);
    }
}
