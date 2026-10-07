using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public class NetworkPlayerRoster
    {
        private readonly List<NetworkPlayer> _players = new();
        private readonly Dictionary<Guid, NetworkPlayer> _playersByGuid = new();

        public IReadOnlyList<NetworkPlayer> Players => _players;

        public void RecordMessage(Guid playerGuid)
        {
            if (_playersByGuid.TryGetValue(playerGuid, out NetworkPlayer player))
            {
                player.TimeSinceLastMessage = 0f;
                return;
            }

            NetworkPlayer newPlayer = new(playerGuid);
            _players.Add(newPlayer);
            _playersByGuid[playerGuid] = newPlayer;
        }

        /// <param name="removedPlayerGuids">Cleared and filled with the players that timed out.</param>
        public void RemoveTimedOut(float unscaledDeltaTime, float timeoutSeconds, List<Guid> removedPlayerGuids)
        {
            removedPlayerGuids.Clear();

            for (int playerIndex = _players.Count - 1; playerIndex >= 0; playerIndex--)
            {
                NetworkPlayer player = _players[playerIndex];
                player.TimeSinceLastMessage += unscaledDeltaTime;

                if (player.TimeSinceLastMessage < timeoutSeconds)
                    continue;

                _players.RemoveAt(playerIndex);
                _playersByGuid.Remove(player.Guid);
                removedPlayerGuids.Add(player.Guid);
            }
        }

        public void Clear()
        {
            _players.Clear();
            _playersByGuid.Clear();
        }
    }
}
