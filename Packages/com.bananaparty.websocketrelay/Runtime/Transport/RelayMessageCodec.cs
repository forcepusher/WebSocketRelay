using System;
using System.Buffers.Binary;
using System.Text;

namespace BananaParty.WebSocketRelay.Transport
{
    public static class RelayMessageCodec
    {
        public const int GuidSize = 16;
        public const int SecretSize = 16;

        public const int ChannelLengthOffset = 1;
        public const int ChannelOffset = 3;

        // Channel message and channel state layout: [type:1][senderGuid:16][channelLength:2][channel][payload].
        // The relay skips channel state for receivers that fell behind, because the next state replaces it.
        public const int ChannelMessageGuidOffset = 1;
        public const int ChannelMessageChannelLengthOffset = ChannelMessageGuidOffset + GuidSize;
        public const int ChannelMessageChannelOffset = ChannelMessageChannelLengthOffset + 2;

        // Ping layout: [type:1][sentTimeSeconds:8]. The relay answers with a Pong carrying the same payload.
        public const int PingMessageSize = 1 + sizeof(double);

        // Hello layout: [type:1][clientGuid:16][secret:16]. The first message on every connection binds it to the client guid,
        // and the secret proves that a later connection with the same guid comes from the same client.
        public const int HelloMessageSize = 1 + GuidSize + SecretSize;

        public static byte[] CreateHelloMessage(Guid clientGuid, ReadOnlySpan<byte> secret)
        {
            if (secret.Length != SecretSize)
                throw new ArgumentException($"The secret must be {SecretSize} bytes.", nameof(secret));

            byte[] message = new byte[HelloMessageSize];
            message[0] = RelayMessageType.Hello;
            WriteGuid(message.AsSpan(1), clientGuid);
            secret.CopyTo(message.AsSpan(1 + GuidSize));
            return message;
        }

        public static byte[] CreatePingMessage(double sentTimeSeconds)
        {
            byte[] message = new byte[PingMessageSize];
            message[0] = RelayMessageType.Ping;
            BinaryPrimitives.WriteInt64LittleEndian(message.AsSpan(1), BitConverter.DoubleToInt64Bits(sentTimeSeconds));
            return message;
        }

        public static bool TryReadPongSentTime(ReadOnlySpan<byte> message, out double sentTimeSeconds)
        {
            if (message.Length != PingMessageSize || message[0] != RelayMessageType.Pong)
            {
                sentTimeSeconds = 0d;
                return false;
            }

            sentTimeSeconds = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(message.Slice(1)));
            return true;
        }

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
            => CreateChannelMessage(RelayMessageType.ChannelMessage, clientId, channel, payload);

        public static byte[] CreateChannelStateMessage(Guid clientId, string channel, ReadOnlySpan<byte> payload = default)
            => CreateChannelMessage(RelayMessageType.ChannelState, clientId, channel, payload);

        private static byte[] CreateChannelMessage(byte type, Guid clientId, string channel, ReadOnlySpan<byte> payload)
        {
            byte[] channelBytes = Encoding.UTF8.GetBytes(channel);
            int payloadOffset = ChannelMessageChannelOffset + channelBytes.Length;
            byte[] message = new byte[payloadOffset + payload.Length];
            message[0] = type;
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
            Span<byte> bytes = stackalloc byte[GuidSize];
            message.Slice(offset, GuidSize).CopyTo(bytes);
            SwapGuidEndianness(bytes);
            return new Guid(bytes);
        }

        public static void WriteGuid(Span<byte> destination, Guid guid)
        {
            guid.TryWriteBytes(destination);
            SwapGuidEndianness(destination);
        }

        // The wire format is big-endian, while Guid stores its first three fields little-endian.
        private static void SwapGuidEndianness(Span<byte> bytes)
        {
            bytes.Slice(0, 4).Reverse();
            bytes.Slice(4, 2).Reverse();
            bytes.Slice(6, 2).Reverse();
        }
    }
}
