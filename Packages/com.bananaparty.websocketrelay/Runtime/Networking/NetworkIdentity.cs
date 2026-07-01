using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkIdentity : MonoBehaviour
    {
        [SerializeField]
        private List<MonoBehaviour> _synchronizedStateMonoBehaviours;
        private List<IState> _synchronizedStates = new();

        private Guid _ownerGuid;
        private string _topic;

        private void Awake()
        {
            foreach (var state in _synchronizedStateMonoBehaviours)
                _synchronizedStates.Add((IState)state);
        }

        public void SetTopic(string topic)
        {
            _topic = topic;
        }

        public void SendBytesMessage(byte[] bytesMessage)
        {

        }

        public void SendStringMessage(byte[] stringMessage)
        {

        }
    }
}
