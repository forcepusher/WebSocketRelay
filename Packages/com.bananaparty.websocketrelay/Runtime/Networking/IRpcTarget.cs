using System;

namespace BananaParty.WebSocketRelay
{
    public interface IRpcTarget
    {
        INetworkIdentity NetworkIdentity { get; }

        string RpcSubjectName { get; }

        /// <param name="senderGuid">
        /// The client that sent the RPC, as checked by the relay, or the local client for an RPC invoked locally.
        /// </param>
        void ReceiveRpc(Guid senderGuid, IStateInput parametersStateInput);
    }
}
