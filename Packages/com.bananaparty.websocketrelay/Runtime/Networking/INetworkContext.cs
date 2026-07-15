using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkContext
    {
        public bool UseBinary {  get; }

        Guid LocalClientIdentity { get; set; }

        IReadOnlyList<INetworkIdentity> NetworkIdentities { get; }

        IReadOnlyList<IAuthorityOrigin> AuthorityOrigins { get; }

        void RegisterNetworkIdentity(INetworkIdentity networkIdentity);

        void UnregisterNetworkIdentity(INetworkIdentity networkIdentity);

        public void RegisterAuthorityOrigin(IAuthorityOrigin authorityOrigin);

        public void UnregisterAuthorityOrigin(IAuthorityOrigin authorityOrigin);

        void RegisterRpcTarget(IRpcTarget rpcTarget);

        void UnregisterRpcTarget(IRpcTarget rpcTarget);

        void ReadNetworkStates(IStateInput stateInput);

        void WriteNetworkStates(IStateOutput stateOutput);
    }
}
