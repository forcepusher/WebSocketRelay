export const RelayMessageType = {
    Subscribe: 0x01,
    Unsubscribe: 0x02,
    Send: 0x03,
    Subscribed: 0x10,
    Unsubscribed: 0x11,
    TopicMessage: 0x12,
    Connected: 0x13,
} as const;

export const RelayMessageGuidSize = 16;

export const RelayMessageTopicLengthOffset = 1;
export const RelayMessageTopicOffset = 3;

export const RelayMessageTopicMessageGuidOffset = 1;
export const RelayMessageTopicMessageTopicLengthOffset = RelayMessageTopicMessageGuidOffset + RelayMessageGuidSize;
export const RelayMessageTopicMessageTopicOffset = RelayMessageTopicMessageTopicLengthOffset + 2;

export const RelayMessageConnectedSize = 1 + RelayMessageGuidSize;

const relayMessageTypeNames: Record<number, string> = {
    [RelayMessageType.Subscribe]: "Subscribe",
    [RelayMessageType.Unsubscribe]: "Unsubscribe",
    [RelayMessageType.Send]: "Send",
    [RelayMessageType.Subscribed]: "Subscribed",
    [RelayMessageType.Unsubscribed]: "Unsubscribed",
    [RelayMessageType.TopicMessage]: "TopicMessage",
    [RelayMessageType.Connected]: "Connected",
};

export function relayMessageTypeName(type: number): string {
    return relayMessageTypeNames[type] ?? `Unknown(0x${type.toString(16)})`;
}

export function relayGuidToBytes(guid: string): Uint8Array {
    const hex = guid.replaceAll("-", "");
    const bytes = new Uint8Array(RelayMessageGuidSize);
    for (let i = 0; i < RelayMessageGuidSize; i++) {
        bytes[i] = Number.parseInt(hex.slice(i * 2, i * 2 + 2), 16);
    }
    return bytes;
}

export function relayReadGuid(message: Uint8Array, offset: number = 1): string {
    const hex = Array.from(message.subarray(offset, offset + RelayMessageGuidSize), (byte) =>
        byte.toString(16).padStart(2, "0"),
    ).join("");
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export function relayReadTopicLength(message: Uint8Array, topicLengthOffset: number = RelayMessageTopicLengthOffset): number {
    if (message.byteLength < topicLengthOffset + 2) return -1;
    return new DataView(message.buffer, message.byteOffset, message.byteLength).getUint16(
        topicLengthOffset,
        true,
    );
}

export function relayPayloadOffset(topicLength: number): number {
    return RelayMessageTopicOffset + topicLength;
}

export function relayTopicMessagePayloadOffset(topicLength: number): number {
    return RelayMessageTopicMessageTopicOffset + topicLength;
}

export function relayReadTopic(message: Uint8Array, topicLengthOffset: number = RelayMessageTopicLengthOffset): string {
    const topicLength = relayReadTopicLength(message, topicLengthOffset);
    if (topicLength < 0) return "";
    const topicOffset = topicLengthOffset + 2;
    return new TextDecoder().decode(
        message.subarray(topicOffset, topicOffset + topicLength),
    );
}

export function relayWriteMessage(type: number, topic: string, payload?: Uint8Array): Uint8Array {
    const topicBytes = new TextEncoder().encode(topic);
    const payloadLength = payload?.byteLength ?? 0;
    const message = new Uint8Array(relayPayloadOffset(topicBytes.byteLength) + payloadLength);
    const view = new DataView(message.buffer);
    view.setUint8(0, type);
    view.setUint16(RelayMessageTopicLengthOffset, topicBytes.byteLength, true);
    message.set(topicBytes, RelayMessageTopicOffset);
    if (payload) message.set(payload, relayPayloadOffset(topicBytes.byteLength));
    return message;
}

export function relayWriteConnectedMessage(clientGuid: string): Uint8Array {
    const message = new Uint8Array(RelayMessageConnectedSize);
    message[0] = RelayMessageType.Connected;
    message.set(relayGuidToBytes(clientGuid), 1);
    return message;
}

export function relayWriteTopicMessage(senderGuid: string, topic: string, payload?: Uint8Array): Uint8Array {
    const topicBytes = new TextEncoder().encode(topic);
    const payloadLength = payload?.byteLength ?? 0;
    const message = new Uint8Array(relayTopicMessagePayloadOffset(topicBytes.byteLength) + payloadLength);
    const view = new DataView(message.buffer);
    view.setUint8(0, RelayMessageType.TopicMessage);
    message.set(relayGuidToBytes(senderGuid), RelayMessageTopicMessageGuidOffset);
    view.setUint16(RelayMessageTopicMessageTopicLengthOffset, topicBytes.byteLength, true);
    message.set(topicBytes, RelayMessageTopicMessageTopicOffset);
    if (payload) message.set(payload, relayTopicMessagePayloadOffset(topicBytes.byteLength));
    return message;
}
