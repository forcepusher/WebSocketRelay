using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        string PrefabName { get; }
        GameObject GameObject { get; }
        string Channel { get; set; }
        Guid NetworkIdentifier { get; set; }
        Guid NetworkAuthorityOwner { get; set; }
        bool NetworkAuthority { get; }
        bool HasAuthorityOwner { get; }
        bool DistanceBasedAuthority { get; }
        bool DestroyWhenAuthorityOwnerLeaves { get; }

        /// <summary>
        /// Placed in a scene rather than spawned, so it outlives sessions and owners instead of being destroyed.
        /// </summary>
        bool IsSceneBound { get; }

        NetworkContext NetworkContext { get; }

        void WriteNetworkState(IStateOutput stateOutput);

        /// <param name="senderGuid">The client that sent the state, as checked by the relay. Only owners sync, so it becomes the owner.</param>
        /// <returns>False when the state is outdated, so it was not applied.</returns>
        bool ReadNetworkState(IStateInput stateInput, Guid senderGuid);

        /// <param name="reliable">
        /// Delivered once and in order to every peer on the channel, also across lost connections and relay restarts.
        /// Peers that join the channel later do not get it, and a peer that timed out misses what was sent until it is back.
        /// </param>
        void SendRpc(string rpcSubjectName, IStateOutput parametersStateOutput, bool invokeLocally = true, bool reliable = false);

        void ClaimAuthority();
    }
}
