using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class MasterClientArbiter : MonoBehaviour, INetworkState
    {
        private NetworkIdentity _networkIdentity;
        [SerializeField]
        private NetworkContext _networkContext;

        private float _localPlayTime;
        private Guid _masterClientGuid = Guid.Empty;
        private readonly Dictionary<Guid, float> _playTimes = new();

        public string NetworkStateName => nameof(MasterClientArbiter);
        public Guid MasterClientGuid => _masterClientGuid;
        public bool IsMasterClient => _masterClientGuid == _networkContext.LocalClientIdentity;

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
        }

        private void Update()
        {
            float unscaledDeltaTime = Time.unscaledDeltaTime;
            _localPlayTime += unscaledDeltaTime;
            _playTimes[_networkContext.LocalClientIdentity] = _localPlayTime;

            IReadOnlyList<NetworkPlayer> activePlayers = _networkContext.NetworkPlayers;
            for (int playerIndex = 0; playerIndex < activePlayers.Count; playerIndex += 1)
            {
                NetworkPlayer activePlayer = activePlayers[playerIndex];
                _playTimes.TryGetValue(activePlayer.Guid, out float remotePlayTime);
                _playTimes[activePlayer.Guid] = remotePlayTime + unscaledDeltaTime;
            }

            ElectMaster();
        }

        public void ReadNetworkState(IStateInput stateInput)
        {
            _ = stateInput.ReadGuid(nameof(_masterClientGuid));
            float reportedPlayTime = stateInput.ReadFloat(nameof(_localPlayTime));

            Guid reporterGuid = _networkIdentity.NetworkOwner;
            if (_playTimes.TryGetValue(reporterGuid, out float knownPlayTime))
                _playTimes[reporterGuid] = Math.Max(knownPlayTime, reportedPlayTime);
            else
                _playTimes[reporterGuid] = reportedPlayTime;

            ElectMaster();
        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {
            stateOutput.WriteGuid(nameof(_masterClientGuid), _masterClientGuid);
            stateOutput.WriteFloat(nameof(_localPlayTime), _localPlayTime);
        }

        private void ElectMaster()
        {
            PruneStalePlayTimes();

            _masterClientGuid = Elect(
                _networkContext.LocalClientIdentity,
                _playTimes,
                _networkContext.NetworkPlayers,
                _masterClientGuid);
        }

        private void PruneStalePlayTimes()
        {
            Guid localClientIdentity = _networkContext.LocalClientIdentity;

            List<Guid> stalePlayerGuids = null;
            foreach (KeyValuePair<Guid, float> playTime in _playTimes)
            {
                if (IsActive(localClientIdentity, _networkContext.NetworkPlayers, playTime.Key))
                    continue;

                stalePlayerGuids ??= new List<Guid>();
                stalePlayerGuids.Add(playTime.Key);
            }

            if (stalePlayerGuids == null)
                return;

            for (int stalePlayerIndex = 0; stalePlayerIndex < stalePlayerGuids.Count; stalePlayerIndex += 1)
                _playTimes.Remove(stalePlayerGuids[stalePlayerIndex]);
        }

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

            if (currentMaster == Guid.Empty || !IsActive(localClientIdentity, alivePlayers, currentMaster))
                return bestCandidate;

            return currentMaster;
        }

        private static bool IsActive(
            Guid localClientIdentity,
            IReadOnlyList<NetworkPlayer> alivePlayers,
            Guid playerGuid)
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
