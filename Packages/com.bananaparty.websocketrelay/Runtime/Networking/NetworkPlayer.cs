using System;

namespace BananaParty.WebSocketRelay
{
    public class NetworkPlayer
    {
        private const float ConnectionTimeoutSeconds = 15f;

        private float _timeSinceLastMessage = 0f;
        public Guid Guid { get; private set; }

        public bool IsTimedOut => _timeSinceLastMessage > ConnectionTimeoutSeconds;

        public NetworkPlayer(Guid playerGuid)
        {
            Guid = playerGuid;
        }

        public void ManualUpdate(float deltaTime)
        {
            _timeSinceLastMessage += deltaTime;
        }

        public void OnTopicMessage(string topic, byte[] data)
        {
            _timeSinceLastMessage = 0f;
        }

        public void SendMessage(byte messageType, byte[] data)
        {
        }
    }
}
