using System;

namespace BananaParty.WebSocketRelay
{
    public class NetworkPlayer
    {
        public Guid Guid { get; private set; }

        public NetworkPlayer(Guid playerGuid)
        {
            Guid = playerGuid;
        }
    }
}
