using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class ClickObjective : MonoBehaviour, INetworkState
    {
        public string NetworkStateName => nameof(ClickObjective);

        private NetworkIdentity _networkIdentity;
        private TextMesh _clickCountText;

        private int _clickCount = 0;
        private int ClickCount
        {
            set
            {
                _clickCount = value;
                _clickCountText.text = value.ToString();
            }
            get => _clickCount;
        }

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
            _clickCountText = GetComponentInChildren<TextMesh>();
        }

        private void OnMouseDown()
        {
            if (!_networkIdentity.NetworkAuthority)
                _networkIdentity.ClaimAuthority();

            ClickCount += 1;
        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {
            stateOutput.WriteInt(nameof(_clickCount), _clickCount);
        }

        public void ReadNetworkState(IStateInput stateInput)
        {
            int clickCount = stateInput.ReadInt(nameof(_clickCount));

            if (!_networkIdentity.NetworkAuthority)
                ClickCount = clickCount;
        }
    }
}
