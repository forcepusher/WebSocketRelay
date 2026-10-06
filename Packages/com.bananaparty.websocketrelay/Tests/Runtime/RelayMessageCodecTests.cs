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
        public void CreateChannelMessage_UsesChannelMessageType()
        {
            byte[] message = RelayMessageCodec.CreateChannelMessage(
                Guid.NewGuid(),
                "chat",
                new byte[] { 0x01 });

            Assert.AreEqual(RelayMessageType.ChannelMessage, message[0]);
        }

        [Test]
        public void CreateProtocolMessage_Subscribe_EncodesChannel()
        {
            byte[] message = RelayMessageCodec.CreateProtocolMessage(RelayMessageType.Subscribe, "lobby");

            Assert.AreEqual(RelayMessageType.Subscribe, message[0]);
            Assert.AreEqual("lobby", RelayMessageCodec.ReadChannel(message));
        }

        [Test]
        public void CreateChannelMessage_EmbedsClientGuidChannelAndPayload()
        {
            Guid clientGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            byte[] payload = { 0xde, 0xad };
            byte[] message = RelayMessageCodec.CreateChannelMessage(clientGuid, "sync", payload);

            Assert.AreEqual(clientGuid, RelayMessageCodec.ReadGuid(message, RelayMessageCodec.ChannelMessageGuidOffset));
            Assert.AreEqual(
                "sync",
                RelayMessageCodec.ReadChannel(message, RelayMessageCodec.ChannelMessageChannelLengthOffset));

            int channelLength = RelayMessageCodec.ReadChannelLength(message, RelayMessageCodec.ChannelMessageChannelLengthOffset);
            int payloadOffset = RelayMessageCodec.GetChannelMessagePayloadOffset(channelLength);
            Assert.AreEqual(0xde, message[payloadOffset]);
            Assert.AreEqual(0xad, message[payloadOffset + 1]);
        }

        [Test]
        public void CreatePingMessage_PongWithSamePayload_RoundTripsSentTime()
        {
            const double sentTimeSeconds = 12345.678901;
            byte[] ping = RelayMessageCodec.CreatePingMessage(sentTimeSeconds);
            byte[] pong = (byte[])ping.Clone();
            pong[0] = RelayMessageType.Pong;

            Assert.AreEqual(RelayMessageType.Ping, ping[0]);
            Assert.AreEqual(RelayMessageCodec.PingMessageSize, ping.Length);
            Assert.IsTrue(RelayMessageCodec.TryReadPongSentTime(pong, out double readSentTimeSeconds));
            Assert.AreEqual(sentTimeSeconds, readSentTimeSeconds);
        }

        [Test]
        public void CreatePingMessage_EncodesLittleEndian()
        {
            byte[] ping = RelayMessageCodec.CreatePingMessage(1d);

            // 1.0 is 0x3FF0000000000000.
            CollectionAssert.AreEqual(new byte[] { RelayMessageType.Ping, 0, 0, 0, 0, 0, 0, 0xF0, 0x3F }, ping);
        }

        [Test]
        public void TryReadPongSentTime_RejectsPing()
        {
            Assert.IsFalse(RelayMessageCodec.TryReadPongSentTime(RelayMessageCodec.CreatePingMessage(1d), out _));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(8)]
        [TestCase(10)]
        public void TryReadPongSentTime_RejectsWrongLength(int length)
        {
            byte[] message = new byte[length];
            if (length > 0)
                message[0] = RelayMessageType.Pong;

            Assert.IsFalse(RelayMessageCodec.TryReadPongSentTime(message, out double sentTimeSeconds));
            Assert.AreEqual(0d, sentTimeSeconds);
        }
    }
}
