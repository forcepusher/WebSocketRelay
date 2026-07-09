using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        string PrefabName { get; }
        GameObject GameObject { get; }
        string Topic { get; set; }
        Guid NetworkIdentifier { get; set; }
        Guid NetworkOwner { get; set; }
        bool NetworkAuthority { get; }
        IReadOnlyList<INetworkState> NetworkStates { get; }
    }
}
