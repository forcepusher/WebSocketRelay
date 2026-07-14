using UnityEditor;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkBinding : MonoBehaviour
    {
        [SerializeField]
        private NetworkChannel _networkChannel;
        [SerializeField]
        private string _guid;

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

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(_guid))
            {
                _guid = System.Guid.NewGuid().ToString();
                EditorUtility.SetDirty(this);
            }
        }
#endif
    }
}
