using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class MasterClient : MonoBehaviour, INetworkState
    {
        public string NetworkStateName => nameof(MasterClient);

        public void ReadNetworkState(IStateInput stateInput)
        {

        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {

        }
    }
}
