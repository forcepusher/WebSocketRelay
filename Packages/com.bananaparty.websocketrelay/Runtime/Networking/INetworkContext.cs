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

        void RegisterNetworkIdentity(INetworkIdentity networkIdentity);

        void UnregisterNetworkIdentity(INetworkIdentity networkIdentity);

        void RegisterAuthorityOrigin(IAuthorityOrigin authorityOrigin);

        void UnregisterAuthorityOrigin(IAuthorityOrigin authorityOrigin);

        void ReadNetworkStates(IStateInput stateInput);

        void WriteNetworkStates(IStateOutput stateOutput);
    }
}
