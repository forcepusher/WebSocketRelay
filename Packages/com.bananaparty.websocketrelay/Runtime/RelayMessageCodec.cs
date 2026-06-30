using System;
using System.Buffers.Binary;
using System.Text;

namespace BananaParty.WebSocketRelay
{
    public static class RelayMessageCodec
    {
        public const int TopicLengthOffset = 1;
        public const int TopicOffset = 3;

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

        public static int ReadTopicLength(ReadOnlySpan<byte> message)
        {
            if (message.Length < TopicOffset)
                return -1;

            return BinaryPrimitives.ReadUInt16LittleEndian(message.Slice(TopicLengthOffset, 2));
        }

        public static int GetPayloadOffset(int topicLength) => TopicOffset + topicLength;

        public static string ReadTopic(ReadOnlySpan<byte> message)
        {
            int topicLength = ReadTopicLength(message);
            if (topicLength < 0)
                return string.Empty;

            return Encoding.UTF8.GetString(message.Slice(TopicOffset, topicLength));
        }
    }
}
