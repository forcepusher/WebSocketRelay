using System;
using System.Collections.Generic;
using System.Text;

namespace BananaParty.WebSocketRelay
{
    public class RpcRouter
    {
        // Rpc message layout: [type:1][rpc body]. Reliable RPCs have a longer header, see ReliableRpcDelivery.
        // Rpc body layout: [subjectNameLength:2][subjectName][networkIdentifier:16][parameters].
        private const int SubjectNameLengthSize = 2;
        private const int NetworkIdentifierSize = 16;
        private const int RpcBodyOffset = 1;

        private readonly IStateFormat _stateFormat;
        private readonly List<IRpcTarget> _rpcTargets = new();
        private readonly Queue<(string channel, byte[] message)> _outgoingMessages = new();
        private readonly ReliableRpcDelivery _reliableDelivery;

        public RpcRouter(IStateFormat stateFormat)
        {
            _stateFormat = stateFormat;
            _reliableDelivery = new ReliableRpcDelivery(DispatchBody);
        }

        public void RegisterTarget(IRpcTarget rpcTarget)
        {
            _rpcTargets.Add(rpcTarget);
        }

        public void UnregisterTarget(IRpcTarget rpcTarget)
        {
            _rpcTargets.Remove(rpcTarget);
        }

        /// <param name="senderGuid">The local client, passed to targets when the RPC is invoked locally.</param>
        /// <param name="reliable">
        /// Delivered once and in order to every peer on the channel, also across lost connections.
        /// </param>
        public void Send(
            Guid senderGuid,
            Guid networkIdentifier,
            string rpcSubjectName,
            IStateOutput parametersStateOutput,
            string channel,
            bool invokeLocally = true,
            bool reliable = false)
        {
            byte[] parametersPayload = _stateFormat.ToPayload(parametersStateOutput);
            byte[] body = CreateBody(networkIdentifier, rpcSubjectName, parametersPayload);

            if (reliable)
            {
                _reliableDelivery.Enqueue(channel, body);
            }
            else
            {
                byte[] message = new byte[RpcBodyOffset + body.Length];
                message[0] = NetworkMessage.Rpc;
                Buffer.BlockCopy(body, 0, message, RpcBodyOffset, body.Length);
                _outgoingMessages.Enqueue((channel, message));
            }

            if (invokeLocally)
                Dispatch(senderGuid, networkIdentifier, rpcSubjectName, parametersPayload);
        }

        public bool TryDequeueOutgoingMessage(out string channel, out byte[] message)
        {
            if (_outgoingMessages.Count == 0)
            {
                channel = null;
                message = null;
                return false;
            }

            (channel, message) = _outgoingMessages.Dequeue();
            return true;
        }

        /// <summary>
        /// Adds the reliable RPCs to send for the first time or again, and the acknowledgements that are due.
        /// </summary>
        public void CollectReliableMessages(double time, List<(string channel, byte[] message)> outgoing)
        {
            _reliableDelivery.CollectOutgoing(time, outgoing);
        }

        /// <param name="senderGuid">The client that sent the message, as checked by the relay.</param>
        public void ProcessIncomingMessage(Guid senderGuid, string channel, byte[] data, Guid localClientGuid)
        {
            switch (data[0])
            {
                case NetworkMessage.Rpc:
                    DispatchBody(senderGuid, data, RpcBodyOffset);
                    break;
                case NetworkMessage.ReliableRpc:
                    _reliableDelivery.ReceiveRpc(senderGuid, channel, data);
                    break;
                case NetworkMessage.RpcAcknowledgement:
                    _reliableDelivery.ReceiveAcknowledgement(senderGuid, channel, data, localClientGuid);
                    break;
                default:
                    throw new InvalidOperationException($"Not an RPC message: {data[0]}");
            }
        }

        /// <summary>
        /// Records a message from a peer, so that reliable RPCs on the channel are kept for it until it acknowledges them.
        /// </summary>
        public void RecordHeard(Guid peerGuid, string channel, double time)
        {
            _reliableDelivery.RecordHeard(peerGuid, channel, time);
        }

        /// <summary>
        /// Stops keeping reliable RPCs for a peer that left, and forgets what it sent.
        /// </summary>
        public void RemovePeer(Guid peerGuid)
        {
            _reliableDelivery.RemovePeer(peerGuid);
        }

        public void ForgetChannel(string channel)
        {
            _reliableDelivery.ForgetChannel(channel);
        }

        public void Clear()
        {
            _outgoingMessages.Clear();
            _reliableDelivery.Clear();
        }

        private static byte[] CreateBody(Guid networkIdentifier, string rpcSubjectName, byte[] parametersPayload)
        {
            byte[] subjectNameBytes = Encoding.UTF8.GetBytes(rpcSubjectName);
            int networkIdentifierOffset = SubjectNameLengthSize + subjectNameBytes.Length;
            int parametersOffset = networkIdentifierOffset + NetworkIdentifierSize;

            byte[] body = new byte[parametersOffset + parametersPayload.Length];
            body[0] = (byte)subjectNameBytes.Length;
            body[1] = (byte)(subjectNameBytes.Length >> 8);
            Buffer.BlockCopy(subjectNameBytes, 0, body, SubjectNameLengthSize, subjectNameBytes.Length);
            Buffer.BlockCopy(networkIdentifier.ToByteArray(), 0, body, networkIdentifierOffset, NetworkIdentifierSize);
            Buffer.BlockCopy(parametersPayload, 0, body, parametersOffset, parametersPayload.Length);
            return body;
        }

        private void DispatchBody(Guid senderGuid, byte[] message, int bodyOffset)
        {
            int subjectNameLength = message[bodyOffset] | (message[bodyOffset + 1] << 8);
            int subjectNameOffset = bodyOffset + SubjectNameLengthSize;
            int networkIdentifierOffset = subjectNameOffset + subjectNameLength;
            int parametersOffset = networkIdentifierOffset + NetworkIdentifierSize;

            string rpcSubjectName = Encoding.UTF8.GetString(message, subjectNameOffset, subjectNameLength);
            Guid networkIdentifier = new(message.AsSpan(networkIdentifierOffset, NetworkIdentifierSize));

            byte[] parametersPayload = new byte[message.Length - parametersOffset];
            Buffer.BlockCopy(message, parametersOffset, parametersPayload, 0, parametersPayload.Length);

            Dispatch(senderGuid, networkIdentifier, rpcSubjectName, parametersPayload);
        }

        private void Dispatch(Guid senderGuid, Guid networkIdentifier, string rpcSubjectName, byte[] parametersPayload)
        {
            // The identifier is matched at dispatch time instead of being indexed at
            // registration time, because a target can register before its identifier
            // is assigned. Unity interleaves Awake/OnEnable across components during
            // scene load, so a component's OnEnable registration can run before
            // NetworkBinding.Awake assigns the identity's NetworkIdentifier.
            foreach (IRpcTarget rpcTarget in _rpcTargets)
            {
                if (rpcTarget.NetworkIdentity.NetworkIdentifier != networkIdentifier)
                    continue;

                if (rpcTarget.RpcSubjectName != rpcSubjectName)
                    continue;

                rpcTarget.ReceiveRpc(senderGuid, _stateFormat.CreateInput(parametersPayload));
            }
        }
    }
}
