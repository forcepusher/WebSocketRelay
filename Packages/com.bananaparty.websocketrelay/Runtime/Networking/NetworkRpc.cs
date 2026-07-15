using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkRpc : MonoBehaviour, INetworkState
    {
        public string NetworkStateName => nameof(NetworkRpc);

        public void ReadNetworkState(IStateInput stateInput)
        {

        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {

        }
    }
}
