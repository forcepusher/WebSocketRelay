using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay.SoakTest
{
    public static class SoakClock
    {
        /// <summary>
        /// Wall clock milliseconds, shared by every client process on the machine so latencies can be measured across processes.
        /// </summary>
        public static long NowMilliseconds => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
    }

    /// <summary>
    /// Player avatar. Its owner moves it between the shared objects so distance based authority keeps changing hands.
    /// </summary>
    public class SoakAvatar : MonoBehaviour, INetworkState
    {
        private NetworkIdentity _networkIdentity;
        private int _sequence;

        /// <summary>
        /// Stops every local avatar, so distance based authority settles and owners can be compared.
        /// </summary>
        public static bool IsMovementFrozen { get; set; }

        public int ClientIndex { get; set; } = -1;

        public string Padding { get; set; } = string.Empty;

        public NetworkIdentity NetworkIdentity => _networkIdentity;

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
        }

        private void Update()
        {
            if (_networkIdentity.NetworkAuthority && ClientIndex >= 0 && !IsMovementFrozen)
                transform.position = SoakClient.GetAvatarPosition(ClientIndex, Time.unscaledTime);
        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {
            stateOutput.WriteInt("Index", ClientIndex);
            stateOutput.WriteInt("Sequence", ++_sequence);
            stateOutput.WriteLong("SentAt", SoakClock.NowMilliseconds);
            stateOutput.WriteVector3("Position", transform.position);
            stateOutput.WriteString("Padding", Padding);
        }

        public void ReadNetworkState(IStateInput stateInput)
        {
            ClientIndex = stateInput.ReadInt("Index");
            stateInput.ReadInt("Sequence");
            long sentAt = stateInput.ReadLong("SentAt");
            Vector3 position = stateInput.ReadVector3("Position");
            Padding = stateInput.ReadString("Padding");

            if (_networkIdentity.NetworkAuthority)
                return;

            transform.position = position;
            SoakClient.Instance?.RecordState(ClientIndex, SoakClock.NowMilliseconds - sentAt);
        }
    }

    /// <summary>
    /// Scene object with distance based authority. Only its owner counts writes, so every client must agree on one owner.
    /// </summary>
    public class SoakShared : MonoBehaviour, INetworkState
    {
        public int Writes { get; private set; }

        public void WriteNetworkState(IStateOutput stateOutput)
        {
            stateOutput.WriteInt("Writes", ++Writes);
        }

        public void ReadNetworkState(IStateInput stateInput)
        {
            Writes = stateInput.ReadInt("Writes");
        }
    }

    /// <summary>
    /// Scene object every client has from the start, so RPCs addressed to it never miss their target
    /// and every missing sequence number is a message the transport lost.
    /// </summary>
    public class SoakHub : MonoBehaviour, IRpcTarget
    {
        public const string TickRpcName = "SoakTick";

        private NetworkIdentity _networkIdentity;

        public INetworkIdentity NetworkIdentity => _networkIdentity;

        public string RpcSubjectName => TickRpcName;

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
        }

        private void OnEnable()
        {
            _networkIdentity.NetworkContext.RegisterRpcTarget(this);
        }

        private void OnDisable()
        {
            _networkIdentity.NetworkContext.UnregisterRpcTarget(this);
        }

        public void SendTick(int clientIndex, int sequence)
        {
            IStateOutput parameters = _networkIdentity.NetworkContext.StateFormat.CreateOutput();
            parameters.WriteInt("Index", clientIndex);
            parameters.WriteInt("Sequence", sequence);
            parameters.WriteLong("SentAt", SoakClock.NowMilliseconds);
            _networkIdentity.SendRpc(TickRpcName, parameters, invokeLocally: false);
        }

        public void ReceiveRpc(Guid senderGuid, IStateInput parametersStateInput)
        {
            int clientIndex = parametersStateInput.ReadInt("Index");
            int sequence = parametersStateInput.ReadInt("Sequence");
            long sentAt = parametersStateInput.ReadLong("SentAt");
            SoakClient.Instance?.RecordRpc(clientIndex, sequence, SoakClock.NowMilliseconds - sentAt);
        }
    }
}
