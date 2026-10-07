namespace BananaParty.WebSocketRelay.Transport
{
    public enum RelayConnectionState
    {
        Disconnected,
        Connecting,
        Connected,

        /// <summary>
        /// The connection was lost and is being restored with the same client guid and subscriptions.
        /// </summary>
        Reconnecting,
    }
}
