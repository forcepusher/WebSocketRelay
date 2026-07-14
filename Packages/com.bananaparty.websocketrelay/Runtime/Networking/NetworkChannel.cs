using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    [CreateAssetMenu]
    public class NetworkChannel : ScriptableObject
    {
        public List<NetworkChannelBinding> _channelBindings;

        public void AddBinding(NetworkChannelBinding channel)
        {
            _channelBindings.Add(channel);
        }

        public void RemoveBinding(NetworkChannelBinding channel)
        {
            _channelBindings.Remove(channel);
        }

        public void SetChannel(string channel)
        {
            foreach (NetworkChannelBinding binding in _channelBindings)
                binding.SetChannel(channel);
        }
    }
}
