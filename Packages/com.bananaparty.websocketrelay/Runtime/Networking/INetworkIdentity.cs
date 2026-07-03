namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentity
    {
        public void WriteState(IStateOutput stateOutput);
        public void ReadState(IStateInput stateInput);
    }
}
