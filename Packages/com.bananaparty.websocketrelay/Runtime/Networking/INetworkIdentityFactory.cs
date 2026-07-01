namespace BananaParty.WebSocketRelay
{
    public interface INetworkIdentityFactory
    {
        NetworkIdentity Instantiate(string resourcePath, string topic);
        void Destroy(NetworkIdentity networkIdentity);
    }
}
