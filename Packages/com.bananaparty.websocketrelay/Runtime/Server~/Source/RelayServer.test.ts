import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { RelayServer } from "./RelayServer";
import {
    RelayMessageType,
    RelayMessageTopicMessageTopicLengthOffset,
    relayReadGuid,
    relayReadTopic,
    relayReadTopicLength,
    relayTopicMessagePayloadOffset,
    relayWriteProtocolMessage,
    relayWriteTopicMessage,
} from "./RelayMessageType";

const testPort = 23145;

function subscribe(ws: WebSocket, topic: string): void {
    ws.send(relayWriteProtocolMessage(RelayMessageType.Subscribe, topic));
}

async function subscribeAndSettle(ws: WebSocket, topic: string): Promise<void> {
    subscribe(ws, topic);
    await new Promise((resolve) => setTimeout(resolve, 10));
}

function unsubscribe(ws: WebSocket, topic: string): void {
    ws.send(relayWriteProtocolMessage(RelayMessageType.Unsubscribe, topic));
}

function sendTopicMessage(ws: WebSocket, senderGuid: string, topic: string, payload: Uint8Array): void {
    ws.send(relayWriteTopicMessage(senderGuid, topic, payload));
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

async function openSocket(clientGuid = crypto.randomUUID()): Promise<{ ws: WebSocket; clientGuid: string }> {
    const ws = new WebSocket(`ws://127.0.0.1:${testPort}`);
    ws.binaryType = "arraybuffer";
    await new Promise<void>((resolve, reject) => {
        ws.onopen = () => resolve();
        ws.onerror = () => reject(new Error("WebSocket connection failed"));
    });

    return { ws, clientGuid };
}

async function expectNoMessage(ws: WebSocket, timeoutMs = 100): Promise<void> {
    let unexpectedMessage = false;
    ws.onmessage = () => {
        unexpectedMessage = true;
    };

    await new Promise((resolve) => setTimeout(resolve, timeoutMs));
    expect(unexpectedMessage).toBe(false);
}

describe("RelayServer", () => {
    const server = new RelayServer(testPort);

    beforeAll(() => {
        server.start();
    });

    afterAll(() => {
        server.stop();
    });

    test("connection does not send messages on open", async () => {
        const { ws } = await openSocket();
        await expectNoMessage(ws);
        ws.close();
    });

    test("subscribe does not send confirmation", async () => {
        const { ws } = await openSocket();
        subscribe(ws, "lobby");
        await expectNoMessage(ws);
        ws.close();
    });

    test("duplicate subscribe does not send a message", async () => {
        const { ws } = await openSocket();
        subscribe(ws, "events");
        await expectNoMessage(ws);

        subscribe(ws, "events");
        await expectNoMessage(ws);

        ws.close();
    });

    test("relays topic messages with client-provided sender guid", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        await subscribeAndSettle(sender.ws, "chat");
        await subscribeAndSettle(receiver.ws, "chat");

        sendTopicMessage(sender.ws, sender.clientGuid, "chat", new Uint8Array([0xaa, 0xbb]));

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

        await subscribeAndSettle(sender.ws, "alpha");
        await subscribeAndSettle(otherTopicClient.ws, "beta");

        sendTopicMessage(sender.ws, sender.clientGuid, "alpha", new Uint8Array([0x01]));

        await expectNoMessage(otherTopicClient.ws);

        sender.ws.close();
        otherTopicClient.ws.close();
    });

    test("relays topic message even when sender is not subscribed to topic", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        await subscribeAndSettle(receiver.ws, "game");

        sendTopicMessage(sender.ws, sender.clientGuid, "game", new Uint8Array([0x99]));

        const response = await receiveBinary(receiver.ws);
        expect(response[0]).toBe(RelayMessageType.TopicMessage);
        expect(relayReadGuid(response, 1)).toBe(sender.clientGuid);

        sender.ws.close();
        receiver.ws.close();
    });

    test("unsubscribe does not send confirmation", async () => {
        const { ws } = await openSocket();

        unsubscribe(ws, "missing");
        await expectNoMessage(ws);

        ws.close();
    });
});
