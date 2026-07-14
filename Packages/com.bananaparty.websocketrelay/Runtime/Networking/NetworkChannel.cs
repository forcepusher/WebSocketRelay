using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    [CreateAssetMenu]
    public class NetworkChannel : ScriptableObject
    {
        public List<NetworkBinding> _channelBindings;

        public void AddBinding(NetworkBinding channel)
        {
            _channelBindings.Add(channel);
        }

        public void RemoveBinding(NetworkBinding channel)
        {
            _channelBindings.Remove(channel);
        }

        public void SetChannel(string channel)
        {
            Debug.Log("SET BINDING TO " + channel);

            foreach (NetworkBinding binding in _channelBindings)
                binding.SetChannel(channel);
        }
    }
}
