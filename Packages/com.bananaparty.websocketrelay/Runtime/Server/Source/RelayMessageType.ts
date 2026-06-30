export const RelayMessageType = {
    Subscribe: 0x01,
    Unsubscribe: 0x02,
    Send: 0x03,
    Subscribed: 0x10,
    Unsubscribed: 0x11,
    TopicMessage: 0x12,
} as const;

export const RelayMessageTopicLengthOffset = 1;
export const RelayMessageTopicOffset = 3;

const relayMessageTypeNames: Record<number, string> = {
    [RelayMessageType.Subscribe]: "Subscribe",
    [RelayMessageType.Unsubscribe]: "Unsubscribe",
    [RelayMessageType.Send]: "Send",
    [RelayMessageType.Subscribed]: "Subscribed",
    [RelayMessageType.Unsubscribed]: "Unsubscribed",
    [RelayMessageType.TopicMessage]: "TopicMessage",
};

export function relayMessageTypeName(type: number): string {
    return relayMessageTypeNames[type] ?? `Unknown(0x${type.toString(16)})`;
}

export function relayReadTopicLength(message: Uint8Array): number {
    if (message.byteLength < RelayMessageTopicOffset) return -1;
    return new DataView(message.buffer, message.byteOffset, message.byteLength).getUint16(
        RelayMessageTopicLengthOffset,
        true,
    );
}

export function relayPayloadOffset(topicLength: number): number {
    return RelayMessageTopicOffset + topicLength;
}

export function relayReadTopic(message: Uint8Array): string {
    const topicLength = relayReadTopicLength(message);
    if (topicLength < 0) return "";
    return new TextDecoder().decode(
        message.subarray(RelayMessageTopicOffset, relayPayloadOffset(topicLength)),
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
