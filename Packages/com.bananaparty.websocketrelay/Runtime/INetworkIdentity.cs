using System;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        Guid OwnerGuid { get; }
    }
}
