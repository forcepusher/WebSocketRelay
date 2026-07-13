using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class MasterClientArbiter : MonoBehaviour, INetworkState
    {
        private NetworkIdentity _networkIdentity;
        private NetworkContext _networkContext;

        private float _localPlayTime;
        private Guid _masterClientGuid = Guid.Empty;
        private readonly Dictionary<Guid, float> _playTimes = new();

        public string NetworkStateName => nameof(MasterClientArbiter);
        public Guid MasterClientGuid => _masterClientGuid;
        public bool IsMasterClient =>
            _masterClientGuid != Guid.Empty &&
            _networkContext != null &&
            _masterClientGuid == _networkContext.LocalClientIdentity;

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
            _networkContext = _networkIdentity.NetworkContext;
        }

        private void Update()
        {
            if (_networkContext == null || _networkContext.LocalClientIdentity == Guid.Empty)
                return;

            float unscaledDeltaTime = Time.unscaledDeltaTime;
            _localPlayTime += unscaledDeltaTime;
            _playTimes[_networkContext.LocalClientIdentity] = _localPlayTime;

            IReadOnlyList<NetworkPlayer> alivePlayers = _networkContext.NetworkPlayers;
            for (int playerIndex = 0; playerIndex < alivePlayers.Count; playerIndex += 1)
            {
                NetworkPlayer alivePlayer = alivePlayers[playerIndex];
                _playTimes.TryGetValue(alivePlayer.Guid, out float remotePlayTime);
                _playTimes[alivePlayer.Guid] = remotePlayTime + unscaledDeltaTime;
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

            _masterClientGuid = MasterClientElection.Elect(
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
                if (MasterClientElection.IsAlive(
                        localClientIdentity,
                        _networkContext.NetworkPlayers,
                        playTime.Key))
                    continue;

                stalePlayerGuids ??= new List<Guid>();
                stalePlayerGuids.Add(playTime.Key);
            }

            if (stalePlayerGuids == null)
                return;

            for (int stalePlayerIndex = 0; stalePlayerIndex < stalePlayerGuids.Count; stalePlayerIndex += 1)
                _playTimes.Remove(stalePlayerGuids[stalePlayerIndex]);
        }
    }
}
