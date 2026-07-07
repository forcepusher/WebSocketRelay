import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { RelayServer } from "./RelayServer";
import {
    RelayMessageType,
    RelayMessageTopicMessageTopicLengthOffset,
    relayReadGuid,
    relayReadTopic,
    relayReadTopicLength,
    relayTopicMessagePayloadOffset,
    relayWriteMessage,
} from "./RelayMessageType";

const testPort = 23145;

function subscribe(ws: WebSocket, topic: string): void {
    ws.send(relayWriteMessage(RelayMessageType.Subscribe, topic));
}

function unsubscribe(ws: WebSocket, topic: string): void {
    ws.send(relayWriteMessage(RelayMessageType.Unsubscribe, topic));
}

function sendTopicMessage(ws: WebSocket, topic: string, payload: Uint8Array): void {
    ws.send(relayWriteMessage(RelayMessageType.Send, topic, payload));
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

async function openSocket(): Promise<{ ws: WebSocket; clientGuid: string }> {
    const ws = new WebSocket(`ws://127.0.0.1:${testPort}`);
    ws.binaryType = "arraybuffer";
    await new Promise<void>((resolve, reject) => {
        ws.onopen = () => resolve();
        ws.onerror = () => reject(new Error("WebSocket connection failed"));
    });

    const connected = await receiveBinary(ws);
    expect(connected[0]).toBe(RelayMessageType.Connected);
    const clientGuid = relayReadGuid(connected);
    return { ws, clientGuid };
}

describe("RelayServer", () => {
    const server = new RelayServer(testPort);

    beforeAll(() => {
        server.start();
    });

    afterAll(() => {
        server.stop();
    });

    test("connection sends CONNECTED with client guid", async () => {
        const { ws, clientGuid } = await openSocket();
        expect(clientGuid).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
        ws.close();
    });

    test("subscribe sends SUBSCRIBED confirmation", async () => {
        const { ws } = await openSocket();
        subscribe(ws, "lobby");

        const response = await receiveBinary(ws);
        expect(response[0]).toBe(RelayMessageType.Subscribed);
        expect(relayReadTopic(response)).toBe("lobby");

        ws.close();
    });

    test("duplicate subscribe does not send another confirmation", async () => {
        const { ws } = await openSocket();
        subscribe(ws, "events");
        await receiveBinary(ws);

        subscribe(ws, "events");
        let duplicateConfirmation = false;
        ws.onmessage = () => {
            duplicateConfirmation = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(duplicateConfirmation).toBe(false);

        ws.close();
    });

    test("relays topic messages to other subscribers", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        subscribe(sender.ws, "chat");
        subscribe(receiver.ws, "chat");
        await receiveBinary(sender.ws);
        await receiveBinary(receiver.ws);

        sendTopicMessage(sender.ws, "chat", new Uint8Array([0xaa, 0xbb]));

        const response = await receiveBinary(receiver.ws);
        expect(response[0]).toBe(RelayMessageType.TopicMessage);
        expect(relayReadGuid(response, 1)).toBe(sender.clientGuid);
        expect(relayReadTopic(response, RelayMessageTopicMessageTopicLengthOffset)).toBe("chat");
        expect(
            Array.from(
                response.subarray(
                    relayTopicMessagePayloadOffset(
                        relayReadTopicLength(response, RelayMessageTopicMessageTopicLengthOffset),
                    ),
                ),
            ),
        ).toEqual([0xaa, 0xbb]);

        sender.ws.close();
        receiver.ws.close();
    });

    test("does not relay to clients on other topics", async () => {
        const sender = await openSocket();
        const otherTopicClient = await openSocket();

        subscribe(sender.ws, "alpha");
        subscribe(otherTopicClient.ws, "beta");
        await receiveBinary(sender.ws);
        await receiveBinary(otherTopicClient.ws);

        sendTopicMessage(sender.ws, "alpha", new Uint8Array([0x01]));

        let unexpectedMessage = false;
        otherTopicClient.ws.onmessage = () => {
            unexpectedMessage = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(unexpectedMessage).toBe(false);

        sender.ws.close();
        otherTopicClient.ws.close();
    });

    test("rejects send from client that unsubscribed", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        subscribe(sender.ws, "game");
        subscribe(receiver.ws, "game");
        await receiveBinary(sender.ws);
        await receiveBinary(receiver.ws);

        unsubscribe(sender.ws, "game");
        await receiveBinary(sender.ws);

        sendTopicMessage(sender.ws, "game", new Uint8Array([0x99]));

        let unexpectedMessage = false;
        receiver.ws.onmessage = () => {
            unexpectedMessage = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(unexpectedMessage).toBe(false);

        sender.ws.close();
        receiver.ws.close();
    });

    test("unsubscribe sends UNSUBSCRIBED only when client was subscribed", async () => {
        const { ws } = await openSocket();

        unsubscribe(ws, "missing");

        let unexpectedMessage = false;
        ws.onmessage = () => {
            unexpectedMessage = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(unexpectedMessage).toBe(false);

        ws.close();
    });
});
