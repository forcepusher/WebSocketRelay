namespace BananaParty.WebSocketRelay
{
    /// <summary>
    /// Component state synchronized by its <see cref="NetworkIdentity"/>. The authority owner writes it, everyone else reads it.
    /// </summary>
    public interface INetworkState
    {
        void WriteNetworkState(IStateOutput stateOutput);

        void ReadNetworkState(IStateInput stateInput);
    }
}
