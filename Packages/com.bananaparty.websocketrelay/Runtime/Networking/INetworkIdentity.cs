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
        int NetworkOwnerVersion { get; set; }
        bool NetworkAuthority { get; }
        IReadOnlyList<INetworkState> NetworkStates { get; }
        bool DistanceBasedAuthority { get; }
        void SendRpc(string rpcSubjectName, IStateOutput parametersStateOutput);

        // Concurrent ownership claims arrive in a different order on each client.
        // Lamport-style versioning with a Guid tie-break makes the outcome order-independent.
        bool TryApplyOwnershipClaim(Guid claimedOwner, int claimVersion)
        {
            if (claimVersion < NetworkOwnerVersion)
                return false;

            if (claimVersion == NetworkOwnerVersion && claimedOwner.CompareTo(NetworkOwner) >= 0)
                return false;

            NetworkOwnerVersion = claimVersion;
            NetworkOwner = claimedOwner;
            return true;
        }
    }
}
