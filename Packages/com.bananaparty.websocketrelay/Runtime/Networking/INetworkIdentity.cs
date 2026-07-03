using System;

namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        string Name { get; }
        Guid Identifier { get; set; }
        Guid Owner { get; set; }
        bool HasAuthority { get; set; }
        void WriteState(IStateOutput stateOutput);
        void ReadState(IStateInput stateInput);
    }
}
