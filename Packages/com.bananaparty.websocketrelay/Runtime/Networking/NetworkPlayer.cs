using System;

namespace BananaParty.WebSocketRelay
{
    public class NetworkPlayer
    {
        private const float ConnectionTimeoutSeconds = 15f;

        private Guid _playerGuid;

        public NetworkPlayer(Guid playerGuid)
        {
            _playerGuid = playerGuid;
        }

        public void ManualUpdate()
        {

        }

        public void SendMessage(byte messageType, byte[] data)
        {
        }
    }
}
