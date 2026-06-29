import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { RelayServer } from "./RelayServer";
import {
    RelayMessageHeaderLength,
    RelayMessagePayloadOffset,
    RelayMessageType,
} from "./RelayMessageType";

const testPort = 23145;

function joinRoom(ws: WebSocket, roomId: number): void {
    const message = new Uint8Array(RelayMessageHeaderLength);
    const view = new DataView(message.buffer);
    view.setUint8(0, RelayMessageType.JoinRoom);
    view.setInt32(1, roomId, true);
    ws.send(message);
}

function leaveRoom(ws: WebSocket, roomId: number): void {
    const message = new Uint8Array(RelayMessageHeaderLength);
    const view = new DataView(message.buffer);
    view.setUint8(0, RelayMessageType.LeaveRoom);
    view.setInt32(1, roomId, true);
    ws.send(message);
}

function sendRoomMessage(ws: WebSocket, roomId: number, payload: Uint8Array): void {
    const message = new Uint8Array(RelayMessagePayloadOffset + payload.byteLength);
    const view = new DataView(message.buffer);
    view.setUint8(0, RelayMessageType.SendMessage);
    view.setInt32(1, roomId, true);
    message.set(payload, RelayMessagePayloadOffset);
    ws.send(message);
}

async function openSocket(): Promise<WebSocket> {
    const ws = new WebSocket(`ws://127.0.0.1:${testPort}`);
    ws.binaryType = "arraybuffer";
    await new Promise<void>((resolve, reject) => {
        ws.onopen = () => resolve();
        ws.onerror = () => reject(new Error("WebSocket connection failed"));
    });
    return ws;
}

async function toUint8Array(data: unknown): Promise<Uint8Array> {
    if (data instanceof ArrayBuffer) return new Uint8Array(data);
    if (data instanceof Uint8Array) return data;
    if (data instanceof Blob) return new Uint8Array(await data.arrayBuffer());
    throw new Error(`Unexpected binary frame type: ${typeof data}`);
}

async function receiveBinary(ws: WebSocket, timeoutMs = 2000): Promise<Uint8Array> {
    return await new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error("Timed out waiting for message")), timeoutMs);

        ws.onmessage = async (event) => {
            clearTimeout(timer);
            resolve(await toUint8Array(event.data));
        };
    });
}

describe("RelayServer", () => {
    const server = new RelayServer(testPort);

    beforeAll(() => {
        server.start();
    });

    afterAll(() => {
        server.stop();
    });

    test("join sends JOINED_ROOM confirmation", async () => {
        const ws = await openSocket();
        joinRoom(ws, 42);

        const response = await receiveBinary(ws);
        expect(response[0]).toBe(RelayMessageType.JoinedRoom);
        expect(new DataView(response.buffer).getInt32(1, true)).toBe(42);

        ws.close();
    });

    test("duplicate join does not send another confirmation", async () => {
        const ws = await openSocket();
        joinRoom(ws, 7);
        await receiveBinary(ws);

        joinRoom(ws, 7);
        let duplicateConfirmation = false;
        ws.onmessage = () => {
            duplicateConfirmation = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(duplicateConfirmation).toBe(false);

        ws.close();
    });

    test("relays room messages to other members", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        joinRoom(sender, 100);
        joinRoom(receiver, 100);
        await receiveBinary(sender);
        await receiveBinary(receiver);

        sendRoomMessage(sender, 100, new Uint8Array([0xaa, 0xbb]));

        const response = await receiveBinary(receiver);
        expect(response[0]).toBe(RelayMessageType.RoomMessage);
        expect(new DataView(response.buffer).getInt32(1, true)).toBe(100);
        expect(Array.from(response.subarray(RelayMessagePayloadOffset))).toEqual([0xaa, 0xbb]);

        sender.close();
        receiver.close();
    });

    test("does not relay to clients in other rooms", async () => {
        const sender = await openSocket();
        const otherRoomClient = await openSocket();

        joinRoom(sender, 200);
        joinRoom(otherRoomClient, 201);
        await receiveBinary(sender);
        await receiveBinary(otherRoomClient);

        sendRoomMessage(sender, 200, new Uint8Array([0x01]));

        let unexpectedMessage = false;
        otherRoomClient.onmessage = () => {
            unexpectedMessage = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(unexpectedMessage).toBe(false);

        sender.close();
        otherRoomClient.close();
    });

    test("rejects send from client that left the room", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        joinRoom(sender, 300);
        joinRoom(receiver, 300);
        await receiveBinary(sender);
        await receiveBinary(receiver);

        leaveRoom(sender, 300);
        await receiveBinary(sender);

        sendRoomMessage(sender, 300, new Uint8Array([0x99]));

        let unexpectedMessage = false;
        receiver.onmessage = () => {
            unexpectedMessage = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(unexpectedMessage).toBe(false);

        sender.close();
        receiver.close();
    });

    test("leave sends LEFT_ROOM only when client was in the room", async () => {
        const ws = await openSocket();

        leaveRoom(ws, 999);

        let unexpectedMessage = false;
        ws.onmessage = () => {
            unexpectedMessage = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(unexpectedMessage).toBe(false);

        ws.close();
    });
});
