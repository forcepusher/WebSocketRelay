namespace BananaParty.WebSocketRelay
{
    public interface IRpcTarget
    {
        string RpcSubjectName { get; }

        void ReceiveRpc(IStateInput parametersStateInput);
    }
}
