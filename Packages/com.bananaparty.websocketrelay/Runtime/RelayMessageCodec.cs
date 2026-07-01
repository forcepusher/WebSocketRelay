using System;
using System.Buffers.Binary;
using System.Text;

namespace BananaParty.WebSocketRelay
{
    public static class RelayMessageCodec
    {
        public const int GuidSize = 16;

        public const int TopicLengthOffset = 1;
        public const int TopicOffset = 3;

        public const int ConnectedMessageSize = 1 + GuidSize;

        public const int TopicMessageGuidOffset = 1;
        public const int TopicMessageTopicLengthOffset = TopicMessageGuidOffset + GuidSize;
        public const int TopicMessageTopicOffset = TopicMessageTopicLengthOffset + 2;

        public static byte[] CreateMessage(byte type, string topic, ReadOnlySpan<byte> payload = default)
        {
            byte[] topicBytes = Encoding.UTF8.GetBytes(topic);
            int payloadOffset = TopicOffset + topicBytes.Length;
            byte[] message = new byte[payloadOffset + payload.Length];
            message[0] = type;
            BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(TopicLengthOffset), (ushort)topicBytes.Length);
            topicBytes.CopyTo(message.AsSpan(TopicOffset));
            payload.CopyTo(message.AsSpan(payloadOffset));
            return message;
        }

        public static int ReadTopicLength(ReadOnlySpan<byte> message, int topicLengthOffset = TopicLengthOffset)
        {
            if (message.Length < topicLengthOffset + 2)
                return -1;

            return BinaryPrimitives.ReadUInt16LittleEndian(message.Slice(topicLengthOffset, 2));
        }

        public static int GetPayloadOffset(int topicLength) => TopicOffset + topicLength;

        public static int GetTopicMessagePayloadOffset(int topicLength) => TopicMessageTopicOffset + topicLength;

        public static string ReadTopic(ReadOnlySpan<byte> message, int topicLengthOffset = TopicLengthOffset)
        {
            int topicLength = ReadTopicLength(message, topicLengthOffset);
            if (topicLength < 0)
                return string.Empty;

            int topicOffset = topicLengthOffset + 2;
            if (message.Length < topicOffset + topicLength)
                return string.Empty;

            return Encoding.UTF8.GetString(message.Slice(topicOffset, topicLength));
        }

        public static Guid ReadGuid(ReadOnlySpan<byte> message, int offset = 1)
        {
            ReadOnlySpan<byte> bytes = message.Slice(offset, GuidSize);
            int a = BinaryPrimitives.ReadInt32BigEndian(bytes);
            short b = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(4));
            short c = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(6));
            return new Guid(a, b, c, bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]);
        }
    }
}
