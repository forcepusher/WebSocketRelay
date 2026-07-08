using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        string PrefabName { get; }
        Guid NetworkIdentifier { get; set; }
        Guid NetworkOwner { get; set; }
        bool NetworkAuthority { get; }
        IReadOnlyList<INetworkState> NetworkStates { get; }
    }
}
