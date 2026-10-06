using System;

namespace BananaParty.WebSocketRelay
{
    public class NetworkPlayer
    {
        public NetworkPlayer(Guid playerGuid)
        {
            Guid = playerGuid;
        }

        public Guid Guid { get; }

        public float TimeSinceLastMessage { get; set; }
    }
}
