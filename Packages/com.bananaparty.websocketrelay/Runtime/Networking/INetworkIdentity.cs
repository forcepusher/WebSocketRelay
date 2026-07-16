using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        string PrefabName { get; }
        GameObject GameObject { get; }
        string Channel { get; set; }
        Guid NetworkIdentifier { get; set; }
        Guid NetworkOwner { get; set; }
        bool NetworkAuthority { get; }
        IReadOnlyList<INetworkState> NetworkStates { get; }
        bool DistanceBasedAuthority { get; }
        void SendRpc(string rpcSubjectName, IStateOutput parametersStateOutput);
    }
}
