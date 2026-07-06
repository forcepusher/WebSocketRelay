using BananaParty.WebSocketRelay;

public interface INetworkState
{
    void WriteState(IStateOutput stateOutput);
    void ReadState(IStateInput stateInput);
}
