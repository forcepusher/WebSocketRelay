using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class MasterClientArbiter : MonoBehaviour, INetworkState
    {
        private NetworkIdentity _networkIdentity;

        public string NetworkStateName => nameof(MasterClientArbiter);

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
        }

        public void ReadNetworkState(IStateInput stateInput)
        {

        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {

        }
    }
}
