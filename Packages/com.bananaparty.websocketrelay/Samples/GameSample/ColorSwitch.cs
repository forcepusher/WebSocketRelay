using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class ColorSwitch : MonoBehaviour, IRpcTarget
    {
        private const string RandomColorParametername = "RandomColor";

        private NetworkIdentity _networkIdentity;

        public INetworkIdentity NetworkIdentity => _networkIdentity;

        private enum RpcType
        {
            RandomColorOnLeftClick,
            GreyColorOnRightClick
        }

        public string RpcSubjectName => nameof(ColorSwitch);

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
        }

        private void Update()
        {
            if (_networkIdentity.NetworkAuthority)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    IStateOutput parametersOutput = _networkIdentity.NetworkContext.StateFormat.CreateOutput();
                    parametersOutput.WriteInt(nameof(RpcType), (int)RpcType.RandomColorOnLeftClick);
                    Vector3 color = new Vector3(Random.value, Random.value, Random.value);
                    parametersOutput.WriteVector3(RandomColorParametername, color);
                    _networkIdentity.SendRpc(RpcSubjectName, parametersOutput);

                    SetColor(color);
                }

                if (Input.GetMouseButtonDown(1))
                {
                    IStateOutput parametersOutput = _networkIdentity.NetworkContext.StateFormat.CreateOutput();
                    parametersOutput.WriteInt(nameof(RpcType), (int)RpcType.GreyColorOnRightClick);
                    _networkIdentity.SendRpc(RpcSubjectName, parametersOutput);

                    SetColor(new Vector3(0.5f, 0.5f, 0.5f));
                }
            }
        }

        private void SetColor(Vector3 color)
        {
            GetComponent<Renderer>().material.color = new Color(color.x, color.y, color.z);
        }

        public void ReceiveRpc(IStateInput parametersStateInput)
        {
            RpcType rpcType = (RpcType)parametersStateInput.ReadInt(nameof(RpcType));
            switch (rpcType)
            {
                case RpcType.RandomColorOnLeftClick:
                    Vector3 color = parametersStateInput.ReadVector3(RandomColorParametername);
                    SetColor(color);
                    break;
                case RpcType.GreyColorOnRightClick:
                    SetColor(new Vector3(0.5f, 0.5f, 0.5f));
                    break;
            }
        }
    }
}
