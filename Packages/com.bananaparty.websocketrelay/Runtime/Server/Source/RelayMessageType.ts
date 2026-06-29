export const RelayMessageType = {
    JoinRoom: 0x01,
    LeaveRoom: 0x02,
    SendMessage: 0x03,
    JoinedRoom: 0x10,
    LeftRoom: 0x11,
    RoomMessage: 0x12,
} as const;

export const RelayMessageHeaderLength = 5;

export const RelayMessagePayloadOffset = 5;

export const RelayRoomTopicPrefix = "room:";

export function relayRoomTopic(roomId: number): string {
    return `${RelayRoomTopicPrefix}${roomId}`;
}

const relayMessageTypeNames: Record<number, string> = {
    [RelayMessageType.JoinRoom]: "JoinRoom",
    [RelayMessageType.LeaveRoom]: "LeaveRoom",
    [RelayMessageType.SendMessage]: "SendMessage",
    [RelayMessageType.JoinedRoom]: "JoinedRoom",
    [RelayMessageType.LeftRoom]: "LeftRoom",
    [RelayMessageType.RoomMessage]: "RoomMessage",
};

export function relayMessageTypeName(type: number): string {
    return relayMessageTypeNames[type] ?? `Unknown(0x${type.toString(16)})`;
}
