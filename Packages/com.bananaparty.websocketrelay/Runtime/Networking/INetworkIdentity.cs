using System;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity : INetworkState
    {
        string PrefabName { get; }
        Guid NetworkIdentifier { get; set; }
        Guid NetworkOwner { get; set; }
        bool NetworkAuthority { get; }
    }
}
