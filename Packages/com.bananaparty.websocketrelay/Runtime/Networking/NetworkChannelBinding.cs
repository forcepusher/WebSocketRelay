using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkChannelBinding : MonoBehaviour
    {
        [SerializeField]
        private NetworkChannel _networkChannel;

        private NetworkIdentity _networkIdentity;

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
        }

        private void OnEnable()
        {
            _networkChannel.AddBinding(this);
        }

        private void OnDisable()
        {
            _networkChannel.RemoveBinding(this);
        }

        public void SetChannel(string channel)
        {
            _networkIdentity.Channel = channel;
        }
    }
}
