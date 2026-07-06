using BananaParty.WebSocketRelay;

public interface INetworkState
{
    string StateName { get; }
    void WriteState(IStateOutput stateOutput);
    void ReadState(IStateInput stateInput);
}
