using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay.SoakTest
{
    /// <summary>
    /// Latency samples and sequence checks for everything received from one remote client.
    /// Window values cover the last report interval, totals the whole run.
    /// </summary>
    public sealed class SoakSenderStats
    {
        public int StateCount;
        public long StateLatencySum;
        public long StateLatencyMax;

        public int RpcCount;
        public int RpcGaps;
        public int RpcDuplicates;
        public long RpcLatencySum;
        public long RpcLatencyMax;

        public int TotalStates;
        public int TotalRpcs;
        public int TotalRpcGaps;
        public int TotalRpcDuplicates;
        public long TotalStateLatencyMax;
        public long TotalRpcLatencyMax;
        public int LastRpcSequence;

        public void RecordState(long latencyMilliseconds)
        {
            StateCount++;
            TotalStates++;
            StateLatencySum += latencyMilliseconds;
            StateLatencyMax = Math.Max(StateLatencyMax, latencyMilliseconds);
            TotalStateLatencyMax = Math.Max(TotalStateLatencyMax, latencyMilliseconds);
        }

        public void RecordRpc(int sequence, long latencyMilliseconds)
        {
            RpcCount++;
            TotalRpcs++;
            RpcLatencySum += latencyMilliseconds;
            RpcLatencyMax = Math.Max(RpcLatencyMax, latencyMilliseconds);
            TotalRpcLatencyMax = Math.Max(TotalRpcLatencyMax, latencyMilliseconds);

            // Sequences start at 1, so the first RPC from a sender that started before this client only counts the RPCs this client missed.
            if (sequence <= LastRpcSequence)
            {
                RpcDuplicates++;
                TotalRpcDuplicates++;
                return;
            }

            if (LastRpcSequence > 0 && sequence > LastRpcSequence + 1)
            {
                RpcGaps += sequence - LastRpcSequence - 1;
                TotalRpcGaps += sequence - LastRpcSequence - 1;
            }

            LastRpcSequence = sequence;
        }

        public void ResetWindow()
        {
            StateCount = 0;
            StateLatencySum = 0;
            StateLatencyMax = 0;
            RpcCount = 0;
            RpcGaps = 0;
            RpcDuplicates = 0;
            RpcLatencySum = 0;
            RpcLatencyMax = 0;
        }
    }

    public sealed class SoakStats
    {
        public readonly SortedDictionary<int, SoakSenderStats> Senders = new();

        public SoakSenderStats For(int senderIndex)
        {
            if (!Senders.TryGetValue(senderIndex, out SoakSenderStats stats))
            {
                stats = new SoakSenderStats();
                Senders[senderIndex] = stats;
            }

            return stats;
        }
    }
}
