using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class ColorSwitch : MonoBehaviour, IRpcTarget
    {
        private const string RandomColorParameterName = "RandomColor";

        private NetworkIdentity _networkIdentity;
        private Renderer _renderer;

        private enum RpcType
        {
            RandomColorOnLeftClick,
            GreyColorOnRightClick,
        }

        public INetworkIdentity NetworkIdentity => _networkIdentity;

        public string RpcSubjectName => nameof(ColorSwitch);

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
            _renderer = GetComponent<Renderer>();
        }

        private void OnEnable()
        {
            _networkIdentity.NetworkContext.RegisterRpcTarget(this);
        }

        private void OnDisable()
        {
            _networkIdentity.NetworkContext.UnregisterRpcTarget(this);
        }

        private void Update()
        {
            if (!_networkIdentity.NetworkAuthority)
                return;

            if (Input.GetMouseButtonDown(0))
            {
                IStateOutput parametersOutput = _networkIdentity.NetworkContext.StateFormat.CreateOutput();
                parametersOutput.WriteInt(nameof(RpcType), (int)RpcType.RandomColorOnLeftClick);
                parametersOutput.WriteColor(RandomColorParameterName, new Color(Random.value, Random.value, Random.value));
                _networkIdentity.SendRpc(RpcSubjectName, parametersOutput);
            }

            if (Input.GetMouseButtonDown(1))
            {
                IStateOutput parametersOutput = _networkIdentity.NetworkContext.StateFormat.CreateOutput();
                parametersOutput.WriteInt(nameof(RpcType), (int)RpcType.GreyColorOnRightClick);
                _networkIdentity.SendRpc(RpcSubjectName, parametersOutput);
            }
        }

        public void ReceiveRpc(IStateInput parametersStateInput)
        {
            switch ((RpcType)parametersStateInput.ReadInt(nameof(RpcType)))
            {
                case RpcType.RandomColorOnLeftClick:
                    _renderer.material.color = parametersStateInput.ReadColor(RandomColorParameterName);
                    break;
                case RpcType.GreyColorOnRightClick:
                    _renderer.material.color = Color.grey;
                    break;
            }
        }
    }
}
