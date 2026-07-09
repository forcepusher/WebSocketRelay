using System;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;

namespace BananaParty.WebSocketRelay.Tests
{
    public class RelayMessageCodecTests
    {
        [Test]
        public void WriteGuid_ReadGuid_RoundTrips()
        {
            Guid guid = Guid.Parse("137bb350-2aac-49c0-9f6a-8041d7e99b5e");
            byte[] message = new byte[RelayMessageCodec.GuidSize];
            RelayMessageCodec.WriteGuid(message, guid);

            Assert.AreEqual(guid, RelayMessageCodec.ReadGuid(message, 0));
        }

        [Test]
        public void CreateTopicMessage_UsesTopicMessageType()
        {
            byte[] message = RelayMessageCodec.CreateTopicMessage(
                Guid.NewGuid(),
                "chat",
                new byte[] { 0x01 });

            Assert.AreEqual(RelayMessageType.TopicMessage, message[0]);
        }

        [Test]
        public void CreateProtocolMessage_Subscribe_EncodesTopic()
        {
            byte[] message = RelayMessageCodec.CreateProtocolMessage(RelayMessageType.Subscribe, "lobby");

            Assert.AreEqual(RelayMessageType.Subscribe, message[0]);
            Assert.AreEqual("lobby", RelayMessageCodec.ReadTopic(message));
        }

        [Test]
        public void CreateTopicMessage_EmbedsClientGuidTopicAndPayload()
        {
            Guid clientGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            byte[] payload = { 0xde, 0xad };
            byte[] message = RelayMessageCodec.CreateTopicMessage(clientGuid, "sync", payload);

            Assert.AreEqual(clientGuid, RelayMessageCodec.ReadGuid(message, RelayMessageCodec.TopicMessageGuidOffset));
            Assert.AreEqual(
                "sync",
                RelayMessageCodec.ReadTopic(message, RelayMessageCodec.TopicMessageTopicLengthOffset));

            int topicLength = RelayMessageCodec.ReadTopicLength(message, RelayMessageCodec.TopicMessageTopicLengthOffset);
            int payloadOffset = RelayMessageCodec.GetTopicMessagePayloadOffset(topicLength);
            Assert.AreEqual(0xde, message[payloadOffset]);
            Assert.AreEqual(0xad, message[payloadOffset + 1]);
        }
    }
}
