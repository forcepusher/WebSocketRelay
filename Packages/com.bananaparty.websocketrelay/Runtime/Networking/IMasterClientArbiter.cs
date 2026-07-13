using System;

namespace BananaParty.WebSocketRelay
{
    public interface IMasterClientArbiter
    {
        Guid MasterClientGuid { get; }
        bool IsMasterClient { get; }
    }
}
