using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class ClickObjective : MonoBehaviour, INetworkState, IRpcTarget
    {
        private const string ClicksParameterName = "Clicks";

        private NetworkIdentity _networkIdentity;
        private TextMesh _clickCountText;
        private int _clickCount;

        public string RpcSubjectName => nameof(ClickObjective);

        public INetworkIdentity NetworkIdentity => _networkIdentity;

        private int ClickCount
        {
            get => _clickCount;
            set
            {
                _clickCount = value;
                _clickCountText.text = value.ToString();
            }
        }

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
            _clickCountText = GetComponentInChildren<TextMesh>();
        }

        private void OnEnable()
        {
            _networkIdentity.NetworkContext.RegisterRpcTarget(this);
        }

        private void OnDisable()
        {
            _networkIdentity.NetworkContext.UnregisterRpcTarget(this);
        }

        private void OnMouseDown()
        {
            // An unowned objective (e.g. before any player claimed it) has no client
            // to apply click RPCs, so the first clicker takes ownership.
            if (!_networkIdentity.HasAuthorityOwner)
                _networkIdentity.ClaimAuthority();

            IStateOutput parametersOutput = _networkIdentity.NetworkContext.StateFormat.CreateOutput();
            parametersOutput.WriteInt(ClicksParameterName, 1);
            _networkIdentity.SendRpc(RpcSubjectName, parametersOutput);
        }

        public void ReceiveRpc(IStateInput parametersStateInput)
        {
            int clicks = parametersStateInput.ReadInt(ClicksParameterName);

            // Clicks are counted only by the authority owner so there is a single
            // writer for the counter. Other clients receive the new count through
            // the owner's state broadcasts, which keeps every client convergent
            // even while distance-based authority transfers ownership around.
            if (_networkIdentity.NetworkAuthority)
                ClickCount += clicks;
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
