import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { RelayServer } from "./RelayServer";
import { RelayMessageType, relayPayloadOffset, relayReadTopic, relayReadTopicLength, relayWriteMessage } from "./RelayMessageType";

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

    test("subscribe sends SUBSCRIBED confirmation", async () => {
        const ws = await openSocket();
        subscribe(ws, "lobby");

        const response = await receiveBinary(ws);
        expect(response[0]).toBe(RelayMessageType.Subscribed);
        expect(relayReadTopic(response)).toBe("lobby");

        ws.close();
    });

    test("duplicate subscribe does not send another confirmation", async () => {
        const ws = await openSocket();
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

        subscribe(sender, "chat");
        subscribe(receiver, "chat");
        await receiveBinary(sender);
        await receiveBinary(receiver);

        sendTopicMessage(sender, "chat", new Uint8Array([0xaa, 0xbb]));

        const response = await receiveBinary(receiver);
        expect(response[0]).toBe(RelayMessageType.TopicMessage);
        expect(relayReadTopic(response)).toBe("chat");
        expect(Array.from(response.subarray(relayPayloadOffset(relayReadTopicLength(response))))).toEqual([
            0xaa, 0xbb,
        ]);

        sender.close();
        receiver.close();
    });

    test("does not relay to clients on other topics", async () => {
        const sender = await openSocket();
        const otherTopicClient = await openSocket();

        subscribe(sender, "alpha");
        subscribe(otherTopicClient, "beta");
        await receiveBinary(sender);
        await receiveBinary(otherTopicClient);

        sendTopicMessage(sender, "alpha", new Uint8Array([0x01]));

        let unexpectedMessage = false;
        otherTopicClient.onmessage = () => {
            unexpectedMessage = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(unexpectedMessage).toBe(false);

        sender.close();
        otherTopicClient.close();
    });

    test("rejects send from client that unsubscribed", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        subscribe(sender, "game");
        subscribe(receiver, "game");
        await receiveBinary(sender);
        await receiveBinary(receiver);

        unsubscribe(sender, "game");
        await receiveBinary(sender);

        sendTopicMessage(sender, "game", new Uint8Array([0x99]));

        let unexpectedMessage = false;
        receiver.onmessage = () => {
            unexpectedMessage = true;
        };

        await new Promise((resolve) => setTimeout(resolve, 100));
        expect(unexpectedMessage).toBe(false);

        sender.close();
        receiver.close();
    });

    test("unsubscribe sends UNSUBSCRIBED only when client was subscribed", async () => {
        const ws = await openSocket();

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
