namespace BananaParty.WebSocketRelay
{
    public interface IRpc
    {
        string RpcSubjectName { get; }

        void ReceiveRpc(IStateInput parametersStateInput);
        void SendRpc(IStateOutput parametersStateOutput);
    }
}
