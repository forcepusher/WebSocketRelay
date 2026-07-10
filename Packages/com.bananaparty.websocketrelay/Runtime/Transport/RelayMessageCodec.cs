using System;
using System.Buffers.Binary;
using System.Text;

namespace BananaParty.WebSocketRelay.Transport
{
    public static class RelayMessageCodec
    {
        public const int GuidSize = 16;

        public const int ChannelLengthOffset = 1;
        public const int ChannelOffset = 3;

        public const int ChannelMessageGuidOffset = 1;
        public const int ChannelMessageChannelLengthOffset = ChannelMessageGuidOffset + GuidSize;
        public const int ChannelMessageChannelOffset = ChannelMessageChannelLengthOffset + 2;

        public static byte[] CreateProtocolMessage(byte type, string channel, ReadOnlySpan<byte> payload = default)
        {
            byte[] channelBytes = Encoding.UTF8.GetBytes(channel);
            int payloadOffset = ChannelOffset + channelBytes.Length;
            byte[] message = new byte[payloadOffset + payload.Length];
            message[0] = type;
            BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(ChannelLengthOffset), (ushort)channelBytes.Length);
            channelBytes.CopyTo(message.AsSpan(ChannelOffset));
            payload.CopyTo(message.AsSpan(payloadOffset));
            return message;
        }

        public static byte[] CreateChannelMessage(Guid clientId, string channel, ReadOnlySpan<byte> payload = default)
        {
            byte[] channelBytes = Encoding.UTF8.GetBytes(channel);
            int payloadOffset = ChannelMessageChannelOffset + channelBytes.Length;
            byte[] message = new byte[payloadOffset + payload.Length];
            message[0] = RelayMessageType.ChannelMessage;
            WriteGuid(message.AsSpan(ChannelMessageGuidOffset), clientId);
            BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(ChannelMessageChannelLengthOffset), (ushort)channelBytes.Length);
            channelBytes.CopyTo(message.AsSpan(ChannelMessageChannelOffset));
            payload.CopyTo(message.AsSpan(payloadOffset));
            return message;
        }

        public static int ReadChannelLength(ReadOnlySpan<byte> message, int channelLengthOffset = ChannelLengthOffset)
        {
            if (message.Length < channelLengthOffset + 2)
                return -1;

            return BinaryPrimitives.ReadUInt16LittleEndian(message.Slice(channelLengthOffset, 2));
        }

        public static int GetPayloadOffset(int channelLength) => ChannelOffset + channelLength;

        public static int GetChannelMessagePayloadOffset(int channelLength) => ChannelMessageChannelOffset + channelLength;

        public static string ReadChannel(ReadOnlySpan<byte> message, int channelLengthOffset = ChannelLengthOffset)
        {
            int channelLength = ReadChannelLength(message, channelLengthOffset);
            if (channelLength < 0)
                return string.Empty;

            int channelOffset = channelLengthOffset + 2;
            if (message.Length < channelOffset + channelLength)
                return string.Empty;

            return Encoding.UTF8.GetString(message.Slice(channelOffset, channelLength));
        }

        public static Guid ReadGuid(ReadOnlySpan<byte> message, int offset = 1)
        {
            ReadOnlySpan<byte> bytes = message.Slice(offset, GuidSize);
            int a = BinaryPrimitives.ReadInt32BigEndian(bytes);
            short b = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(4));
            short c = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(6));
            return new Guid(a, b, c, bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]);
        }

        public static void WriteGuid(Span<byte> destination, Guid guid)
        {
            ReadOnlySpan<char> hex = guid.ToString("N");

            for (int i = 0; i < GuidSize; i++)
            {
                destination[i] = (byte)((FromHex(hex[i * 2]) << 4) | FromHex(hex[i * 2 + 1]));
            }
        }

        private static int FromHex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new ArgumentException($"Invalid hex character: {c}");
        }
    }
}
