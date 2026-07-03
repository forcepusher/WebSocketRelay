namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        bool HasAuthority { get; set; }
        void WriteState(IStateOutput stateOutput);
        void ReadState(IStateInput stateInput);
    }
}
