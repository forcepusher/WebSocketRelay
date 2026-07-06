using System;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity : INetworkState
    {
        Guid NetworkIdentifier { get; set; }
        Guid NetworkOwner { get; set; }
        bool NetworkAuthority { get; set; }
    }
}
