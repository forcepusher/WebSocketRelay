namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        public bool HasAuthority { get; set; }
        public void WriteState(IStateOutput stateOutput);
        public void ReadState(IStateInput stateInput);
    }
}
