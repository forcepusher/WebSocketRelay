using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        string PrefabName { get; }
        string Topic { get; set; }
        Guid NetworkIdentifier { get; set; }
        Guid NetworkOwner { get; set; }
        bool NetworkAuthority { get; }
        IReadOnlyList<INetworkState> NetworkStates { get; }
    }
}
