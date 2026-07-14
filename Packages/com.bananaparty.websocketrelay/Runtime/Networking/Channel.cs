using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    [CreateAssetMenu]
    public class Channel : ScriptableObject
    {
        public List<ChannelBinding> _channelBindings;

        public void AddBinding(ChannelBinding channel)
        {
            _channelBindings.Add(channel);
        }

        public void RemoveBinding(ChannelBinding channel)
        {
            _channelBindings.Remove(channel);
        }

        public void SetChannel(string channel)
        {
            foreach (ChannelBinding binding in _channelBindings)
                binding.SetChannel(channel);
        }
    }
}
