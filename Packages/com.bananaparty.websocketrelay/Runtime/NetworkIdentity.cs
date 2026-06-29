using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkIdentity : MonoBehaviour, INetworkIdentity
    {
        private GuidState _ownerGuid;

        public Guid OwnerGuid => _ownerGuid.Value;
    }
}
