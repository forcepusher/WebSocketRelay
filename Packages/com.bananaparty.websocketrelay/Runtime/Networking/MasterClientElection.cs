using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public static class MasterClientElection
    {
        public static Guid Elect(
            Guid localClientIdentity,
            IReadOnlyDictionary<Guid, float> playTimes,
            IReadOnlyList<NetworkPlayer> alivePlayers,
            Guid currentMaster)
        {
            Guid bestCandidate = Guid.Empty;
            float bestScore = float.MinValue;

            if (localClientIdentity != Guid.Empty)
                TryCandidate(localClientIdentity, GetPlayTime(playTimes, localClientIdentity), ref bestCandidate, ref bestScore);

            for (int playerIndex = 0; playerIndex < alivePlayers.Count; playerIndex += 1)
            {
                NetworkPlayer alivePlayer = alivePlayers[playerIndex];
                TryCandidate(alivePlayer.Guid, GetPlayTime(playTimes, alivePlayer.Guid), ref bestCandidate, ref bestScore);
            }

            if (bestCandidate == Guid.Empty)
                return Guid.Empty;

            if (currentMaster == Guid.Empty || !IsAlive(localClientIdentity, alivePlayers, currentMaster))
                return bestCandidate;

            return currentMaster;
        }

        public static bool IsAlive(Guid localClientIdentity, IReadOnlyList<NetworkPlayer> alivePlayers, Guid playerGuid)
        {
            if (playerGuid == Guid.Empty)
                return false;

            if (playerGuid == localClientIdentity)
                return true;

            for (int playerIndex = 0; playerIndex < alivePlayers.Count; playerIndex += 1)
            {
                if (alivePlayers[playerIndex].Guid == playerGuid)
                    return true;
            }

            return false;
        }

        private static float GetPlayTime(IReadOnlyDictionary<Guid, float> playTimes, Guid playerGuid)
        {
            return playTimes.TryGetValue(playerGuid, out float playTime)
                ? playTime
                : 0f;
        }

        private static void TryCandidate(Guid candidateGuid, float playTime, ref Guid bestCandidate, ref float bestScore)
        {
            if (candidateGuid == Guid.Empty)
                return;

            if (playTime > bestScore)
            {
                bestCandidate = candidateGuid;
                bestScore = playTime;
                return;
            }

            if (playTime < bestScore)
                return;

            if (bestCandidate == Guid.Empty || candidateGuid.CompareTo(bestCandidate) < 0)
                bestCandidate = candidateGuid;
        }
    }
}
