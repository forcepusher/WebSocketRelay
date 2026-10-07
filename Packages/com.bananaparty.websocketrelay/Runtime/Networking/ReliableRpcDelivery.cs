using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    /// <summary>
    /// Delivers reliable RPCs once and in order per sender and channel, also across lost connections.
    /// Senders number them and keep them until every peer on the channel acknowledged them, receivers
    /// acknowledge what arrived, and senders resend what a peer that is heard again has not acknowledged.
    /// </summary>
    internal sealed class ReliableRpcDelivery
    {
        // Reliable RPC layout: [type:1][epoch:4][sequence:4][oldestRetainedSequence:4][rpc body].
        public const int BodyOffset = 13;

        // Acknowledgement layout: [type:1][count:2], then per sender [senderGuid:16][epoch:4][sequence:4].
        private const int AcknowledgementHeaderSize = 3;
        private const int AcknowledgementEntrySize = 24;

        private const double AcknowledgementIntervalSeconds = 0.1;

        // Receivers acknowledge at least this often, so that senders hear peers that send nothing else and resend to them.
        private const double KeepAliveIntervalSeconds = 1d;

        // A peer heard from this long after an RPC was sent without acknowledging it has missed the RPC.
        private const double ResendDelaySeconds = 1d;

        private const int MaxUnacknowledgedPerChannel = 4096;
        private const int MaxPendingPerSender = 4096;

        private readonly Action<Guid, byte[], int> _deliver;
        private readonly System.Random _random = new();
        private readonly Dictionary<string, OutgoingStream> _outgoingStreams = new();
        private readonly Dictionary<string, Dictionary<Guid, IncomingStream>> _incomingStreams = new();
        private readonly Dictionary<string, double> _lastAcknowledgementTimes = new();
        private readonly Dictionary<Guid, double> _lastHeardTimes = new();

        /// <param name="deliver">Receives the sender, the reliable RPC message and the offset of its RPC body.</param>
        public ReliableRpcDelivery(Action<Guid, byte[], int> deliver)
        {
            _deliver = deliver;
        }

        public void Enqueue(string channel, byte[] rpcBody)
        {
            OutgoingStream stream = GetOrCreateOutgoingStream(channel);
            if (stream.Unacknowledged.Count >= MaxUnacknowledgedPerChannel)
            {
                // Peers still missing it skip it, because the oldest retained sequence moves past it.
                stream.Unacknowledged.RemoveAt(0);
                if (!stream.HasWarnedAboutOverflow)
                {
                    stream.HasWarnedAboutOverflow = true;
                    Debug.LogWarning($"More than {MaxUnacknowledgedPerChannel} reliable RPCs on channel '{channel}' are waiting "
                        + "for acknowledgements, so the oldest are given up.");
                }
            }

            stream.Unacknowledged.Add(new OutgoingRpc(stream.NextSequence++, rpcBody));
        }

        /// <summary>
        /// Records a message from a peer, so that RPCs on the channel are kept for it until it acknowledges them.
        /// </summary>
        public void RecordHeard(Guid peerGuid, string channel, double time)
        {
            _lastHeardTimes[peerGuid] = time;

            // A peer that just appeared only needs the RPCs sent from now on.
            OutgoingStream stream = GetOrCreateOutgoingStream(channel);
            if (!stream.AcknowledgedByPeer.ContainsKey(peerGuid))
                stream.AcknowledgedByPeer[peerGuid] = stream.NextSequence - 1;
        }

        public void ReceiveRpc(Guid senderGuid, string channel, byte[] message)
        {
            if (message.Length < BodyOffset)
                throw new InvalidOperationException("Reliable RPC message is too short.");

            uint epoch = ReadUInt32(message, 1);
            uint sequence = ReadUInt32(message, 5);
            uint oldestRetainedSequence = ReadUInt32(message, 9);

            IncomingStream stream = GetOrCreateIncomingStream(senderGuid, channel);
            stream.IsAcknowledgementDue = true;

            // A new epoch means the sender started over, e.g. after clearing its session.
            if (!stream.HasBaseline || stream.Epoch != epoch)
                stream.Reset(epoch, oldestRetainedSequence);

            // The sender gave up on what this client misses, e.g. because it dropped this client as timed out meanwhile.
            if (oldestRetainedSequence > stream.NextExpectedSequence)
                SkipTo(senderGuid, channel, stream, oldestRetainedSequence);

            if (stream.Pending.Count >= MaxPendingPerSender)
                SkipTo(senderGuid, channel, stream, stream.SmallestPendingSequence());

            if (sequence < stream.NextExpectedSequence || stream.Pending.ContainsKey(sequence))
                return;

            stream.Pending.Add(sequence, message);
            DeliverInOrder(senderGuid, stream);
        }

        public void ReceiveAcknowledgement(Guid peerGuid, string channel, byte[] message, Guid localClientGuid)
        {
            if (!_outgoingStreams.TryGetValue(channel, out OutgoingStream stream) || message.Length < AcknowledgementHeaderSize)
                return;

            int count = BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(1));
            for (int entryIndex = 0; entryIndex < count; entryIndex++)
            {
                int offset = AcknowledgementHeaderSize + entryIndex * AcknowledgementEntrySize;
                if (offset + AcknowledgementEntrySize > message.Length)
                    break;

                if (new Guid(message.AsSpan(offset, 16)) != localClientGuid || ReadUInt32(message, offset + 16) != stream.Epoch)
                    continue;

                // Acknowledgements from one peer only grow, so the latest one is where the peer is.
                stream.AcknowledgedByPeer[peerGuid] = ReadUInt32(message, offset + 20);
            }

            Prune(stream);
        }

        /// <summary>
        /// Adds the reliable RPCs to send for the first time or again, and the acknowledgements that are due.
        /// </summary>
        public void CollectOutgoing(double time, List<(string channel, byte[] message)> outgoing)
        {
            foreach ((string channel, OutgoingStream stream) in _outgoingStreams)
            {
                foreach (OutgoingRpc rpc in stream.Unacknowledged)
                {
                    if (rpc.HasBeenSent && !IsMissedByHeardPeer(stream, rpc))
                        continue;

                    outgoing.Add((channel, CreateRpcMessage(stream, rpc)));
                    rpc.SentTime = time;
                    rpc.HasBeenSent = true;
                }

                Prune(stream);
            }

            foreach ((string channel, Dictionary<Guid, IncomingStream> streams) in _incomingStreams)
            {
                double sinceLastAcknowledgement = time - _lastAcknowledgementTimes.GetValueOrDefault(channel, double.NegativeInfinity);
                bool isDue = sinceLastAcknowledgement >= KeepAliveIntervalSeconds
                    || (sinceLastAcknowledgement >= AcknowledgementIntervalSeconds && HasDueAcknowledgement(streams));
                if (!isDue)
                    continue;

                outgoing.Add((channel, CreateAcknowledgementMessage(streams)));
                _lastAcknowledgementTimes[channel] = time;
                foreach (IncomingStream stream in streams.Values)
                    stream.IsAcknowledgementDue = false;
            }
        }

        /// <summary>
        /// Stops keeping RPCs for a peer and forgets what it sent, e.g. once it timed out.
        /// </summary>
        public void RemovePeer(Guid peerGuid)
        {
            _lastHeardTimes.Remove(peerGuid);

            foreach (OutgoingStream stream in _outgoingStreams.Values)
            {
                if (stream.AcknowledgedByPeer.Remove(peerGuid))
                    Prune(stream);
            }

            foreach (Dictionary<Guid, IncomingStream> streams in _incomingStreams.Values)
                streams.Remove(peerGuid);
        }

        public void ForgetChannel(string channel)
        {
            _outgoingStreams.Remove(channel);
            _incomingStreams.Remove(channel);
            _lastAcknowledgementTimes.Remove(channel);
        }

        public void Clear()
        {
            foreach (Dictionary<Guid, IncomingStream> streams in _incomingStreams.Values)
            {
                foreach (IncomingStream stream in streams.Values)
                    stream.Pending.Clear();
            }

            _outgoingStreams.Clear();
            _incomingStreams.Clear();
            _lastAcknowledgementTimes.Clear();
            _lastHeardTimes.Clear();
        }

        private static uint ReadUInt32(byte[] message, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(offset));

        private static void WriteUInt32(byte[] message, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(offset), value);

        private static void Prune(OutgoingStream stream)
        {
            // Without peers on the channel there is no one to wait for.
            uint acknowledgedByAll = uint.MaxValue;
            foreach (uint acknowledged in stream.AcknowledgedByPeer.Values)
                acknowledgedByAll = Math.Min(acknowledgedByAll, acknowledged);

            int prunedCount = 0;
            while (prunedCount < stream.Unacknowledged.Count
                && stream.Unacknowledged[prunedCount].HasBeenSent
                && stream.Unacknowledged[prunedCount].Sequence <= acknowledgedByAll)
                prunedCount++;

            stream.Unacknowledged.RemoveRange(0, prunedCount);
        }

        private static bool HasDueAcknowledgement(Dictionary<Guid, IncomingStream> streams)
        {
            foreach (IncomingStream stream in streams.Values)
            {
                if (stream.IsAcknowledgementDue)
                    return true;
            }

            return false;
        }

        private static byte[] CreateRpcMessage(OutgoingStream stream, OutgoingRpc rpc)
        {
            byte[] message = new byte[BodyOffset + rpc.Body.Length];
            message[0] = NetworkMessage.ReliableRpc;
            WriteUInt32(message, 1, stream.Epoch);
            WriteUInt32(message, 5, rpc.Sequence);
            WriteUInt32(message, 9, stream.Unacknowledged[0].Sequence);
            Buffer.BlockCopy(rpc.Body, 0, message, BodyOffset, rpc.Body.Length);
            return message;
        }

        private static byte[] CreateAcknowledgementMessage(Dictionary<Guid, IncomingStream> streams)
        {
            byte[] message = new byte[AcknowledgementHeaderSize + streams.Count * AcknowledgementEntrySize];
            message[0] = NetworkMessage.RpcAcknowledgement;
            BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(1), (ushort)streams.Count);

            int offset = AcknowledgementHeaderSize;
            foreach ((Guid senderGuid, IncomingStream stream) in streams)
            {
                senderGuid.TryWriteBytes(message.AsSpan(offset));
                WriteUInt32(message, offset + 16, stream.Epoch);
                WriteUInt32(message, offset + 20, stream.NextExpectedSequence - 1);
                offset += AcknowledgementEntrySize;
            }

            return message;
        }

        private bool IsMissedByHeardPeer(OutgoingStream stream, OutgoingRpc rpc)
        {
            foreach ((Guid peerGuid, uint acknowledged) in stream.AcknowledgedByPeer)
            {
                if (acknowledged < rpc.Sequence
                    && _lastHeardTimes.TryGetValue(peerGuid, out double lastHeardTime)
                    && lastHeardTime - rpc.SentTime >= ResendDelaySeconds)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Moves past RPCs that will not come anymore. Those among them that did arrive are still delivered, in order.
        /// </summary>
        private void SkipTo(Guid senderGuid, string channel, IncomingStream stream, uint sequence)
        {
            List<uint> arrivedSequences = new();
            foreach (uint pendingSequence in stream.Pending.Keys)
            {
                if (pendingSequence < sequence)
                    arrivedSequences.Add(pendingSequence);
            }

            arrivedSequences.Sort();
            uint missedCount = sequence - stream.NextExpectedSequence - (uint)arrivedSequences.Count;
            Debug.LogWarning($"Missed {missedCount} reliable RPCs from {senderGuid} on channel '{channel}' that the sender gave up on.");

            foreach (uint arrivedSequence in arrivedSequences)
            {
                byte[] message = stream.Pending[arrivedSequence];
                stream.Pending.Remove(arrivedSequence);
                Deliver(senderGuid, message);
            }

            stream.NextExpectedSequence = sequence;
            DeliverInOrder(senderGuid, stream);
        }

        private void DeliverInOrder(Guid senderGuid, IncomingStream stream)
        {
            while (stream.Pending.TryGetValue(stream.NextExpectedSequence, out byte[] message))
            {
                stream.Pending.Remove(stream.NextExpectedSequence);
                stream.NextExpectedSequence++;
                Deliver(senderGuid, message);
            }
        }

        private void Deliver(Guid senderGuid, byte[] message)
        {
            try
            {
                _deliver(senderGuid, message, BodyOffset);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private OutgoingStream GetOrCreateOutgoingStream(string channel)
        {
            if (_outgoingStreams.TryGetValue(channel, out OutgoingStream stream))
                return stream;

            // Random, so receivers tell a restarted sender apart from the one they knew.
            stream = new OutgoingStream((uint)_random.Next(1, int.MaxValue));
            _outgoingStreams.Add(channel, stream);
            return stream;
        }

        private IncomingStream GetOrCreateIncomingStream(Guid senderGuid, string channel)
        {
            if (!_incomingStreams.TryGetValue(channel, out Dictionary<Guid, IncomingStream> streams))
            {
                streams = new Dictionary<Guid, IncomingStream>();
                _incomingStreams.Add(channel, streams);
            }

            if (!streams.TryGetValue(senderGuid, out IncomingStream stream))
            {
                stream = new IncomingStream();
                streams.Add(senderGuid, stream);
            }

            return stream;
        }

        private sealed class OutgoingStream
        {
            public OutgoingStream(uint epoch)
            {
                Epoch = epoch;
            }

            public uint Epoch { get; }

            public uint NextSequence { get; set; } = 1;

            public List<OutgoingRpc> Unacknowledged { get; } = new();

            /// <summary>
            /// The peers on the channel and the highest sequence each received everything up to.
            /// </summary>
            public Dictionary<Guid, uint> AcknowledgedByPeer { get; } = new();

            public bool HasWarnedAboutOverflow { get; set; }
        }

        private sealed class OutgoingRpc
        {
            public OutgoingRpc(uint sequence, byte[] body)
            {
                Sequence = sequence;
                Body = body;
            }

            public uint Sequence { get; }

            public byte[] Body { get; }

            public bool HasBeenSent { get; set; }

            public double SentTime { get; set; }
        }

        private sealed class IncomingStream
        {
            public uint Epoch { get; private set; }

            public bool HasBaseline { get; private set; }

            public uint NextExpectedSequence { get; set; }

            public Dictionary<uint, byte[]> Pending { get; } = new();

            public bool IsAcknowledgementDue { get; set; }

            /// <summary>
            /// Starts at the oldest RPC the sender still keeps, which covers everything sent since it heard this client.
            /// </summary>
            public void Reset(uint epoch, uint oldestRetainedSequence)
            {
                Epoch = epoch;
                HasBaseline = true;
                NextExpectedSequence = oldestRetainedSequence;
                Pending.Clear();
            }

            public uint SmallestPendingSequence()
            {
                uint smallest = uint.MaxValue;
                foreach (uint pendingSequence in Pending.Keys)
                    smallest = Math.Min(smallest, pendingSequence);

                return smallest;
            }
        }
    }
}
