import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import net from "node:net";
import { RelayServer } from "./RelayServer";
import {
    RelayMessageType,
    RelayMessageChannelMessageChannelLengthOffset,
    RelayMessagePingMaxSize,
    relayReadGuid,
    relayReadChannel,
    relayReadChannelLength,
    relayChannelMessagePayloadOffset,
    relayWriteProtocolMessage,
    relayWriteChannelMessage,
    relayWritePingMessage,
} from "./RelayMessageType";

const testPort = 23145;

function subscribe(ws: WebSocket, channel: string): void {
    ws.send(relayWriteProtocolMessage(RelayMessageType.Subscribe, channel));
}

async function subscribeAndSettle(ws: WebSocket, channel: string): Promise<void> {
    subscribe(ws, channel);
    await new Promise((resolve) => setTimeout(resolve, 10));
}

function unsubscribe(ws: WebSocket, channel: string): void {
    ws.send(relayWriteProtocolMessage(RelayMessageType.Unsubscribe, channel));
}

function sendChannelMessage(ws: WebSocket, senderGuid: string, channel: string, payload: Uint8Array): void {
    ws.send(relayWriteChannelMessage(senderGuid, channel, payload));
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

async function openSocket(
    clientGuid = crypto.randomUUID(),
    port = testPort,
): Promise<{ ws: WebSocket; clientGuid: string }> {
    const ws = new WebSocket(`ws://127.0.0.1:${port}`);
    ws.binaryType = "arraybuffer";
    await new Promise<void>((resolve, reject) => {
        ws.onopen = () => resolve();
        ws.onerror = () => reject(new Error("WebSocket connection failed"));
    });

    return { ws, clientGuid };
}

async function waitFor(condition: () => boolean, timeoutMs: number): Promise<void> {
    const deadline = Date.now() + timeoutMs;
    while (!condition() && Date.now() < deadline) await Bun.sleep(10);
}

// Client frames must be masked (RFC 6455 5.3). Only short payloads are needed here.
function maskedBinaryFrame(payload: Uint8Array): Uint8Array {
    if (payload.byteLength > 125) throw new Error("Payload too large for a short frame");
    const mask = crypto.getRandomValues(new Uint8Array(4));
    const frame = new Uint8Array(6 + payload.byteLength);
    frame[0] = 0x82;
    frame[1] = 0x80 | payload.byteLength;
    frame.set(mask, 2);
    for (let i = 0; i < payload.byteLength; i++) frame[6 + i] = payload[i]! ^ mask[i % 4]!;
    return frame;
}

// A raw WebSocket client that subscribes and then stops reading, like a client on a stalled link.
async function openStalledSubscriber(port: number, channel: string): Promise<net.Socket> {
    const socket = net.connect(port, "127.0.0.1");
    await new Promise<void>((resolve, reject) => {
        socket.once("connect", () => resolve());
        socket.once("error", reject);
    });

    const key = Buffer.from(crypto.getRandomValues(new Uint8Array(16))).toString("base64");
    socket.write(
        `GET / HTTP/1.1\r\nHost: 127.0.0.1:${port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n` +
            `Sec-WebSocket-Key: ${key}\r\nSec-WebSocket-Version: 13\r\n\r\n`,
    );

    await new Promise<void>((resolve) => {
        let response = "";
        const onData = (chunk: Buffer) => {
            response += chunk.toString("latin1");
            if (!response.includes("\r\n\r\n")) return;
            socket.off("data", onData);
            resolve();
        };
        socket.on("data", onData);
    });

    socket.pause();
    socket.write(maskedBinaryFrame(relayWriteProtocolMessage(RelayMessageType.Subscribe, channel)));
    return socket;
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

    test("relays channel messages with client-provided sender guid", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        await subscribeAndSettle(sender.ws, "chat");
        await subscribeAndSettle(receiver.ws, "chat");

        sendChannelMessage(sender.ws, sender.clientGuid, "chat", new Uint8Array([0xaa, 0xbb]));

        const response = await receiveBinary(receiver.ws);
        expect(response[0]).toBe(RelayMessageType.ChannelMessage);
        expect(relayReadGuid(response, 1)).toBe(sender.clientGuid);
        expect(relayReadChannel(response, RelayMessageChannelMessageChannelLengthOffset)).toBe("chat");
        expect(
            Array.from(
                response.subarray(
                    relayChannelMessagePayloadOffset(
                        relayReadChannelLength(response, RelayMessageChannelMessageChannelLengthOffset),
                    ),
                ),
            ),
        ).toEqual([0xaa, 0xbb]);

        sender.ws.close();
        receiver.ws.close();
    });

    test("does not relay to clients on other channels", async () => {
        const sender = await openSocket();
        const otherChannelClient = await openSocket();

        await subscribeAndSettle(sender.ws, "alpha");
        await subscribeAndSettle(otherChannelClient.ws, "beta");

        sendChannelMessage(sender.ws, sender.clientGuid, "alpha", new Uint8Array([0x01]));

        await expectNoMessage(otherChannelClient.ws);

        sender.ws.close();
        otherChannelClient.ws.close();
    });

    test("relays channel message even when sender is not subscribed to channel", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        await subscribeAndSettle(receiver.ws, "game");

        sendChannelMessage(sender.ws, sender.clientGuid, "game", new Uint8Array([0x99]));

        const response = await receiveBinary(receiver.ws);
        expect(response[0]).toBe(RelayMessageType.ChannelMessage);
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

    test("does not echo channel messages back to the sender", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();

        await subscribeAndSettle(sender.ws, "echo");
        await subscribeAndSettle(receiver.ws, "echo");

        const senderSilence = expectNoMessage(sender.ws, 200);
        sendChannelMessage(sender.ws, sender.clientGuid, "echo", new Uint8Array([0x42]));

        const response = await receiveBinary(receiver.ws);
        expect(relayReadGuid(response, 1)).toBe(sender.clientGuid);
        await senderSilence;

        sender.ws.close();
        receiver.ws.close();
    });

    test("answers ping with pong carrying the same payload", async () => {
        const { ws } = await openSocket();
        const payload = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8]);

        ws.send(relayWritePingMessage(payload));
        const response = await receiveBinary(ws);

        expect(response[0]).toBe(RelayMessageType.Pong);
        expect(Array.from(response.subarray(1))).toEqual(Array.from(payload));

        ws.close();
    });

    test("answers ping without payload", async () => {
        const { ws } = await openSocket();

        ws.send(relayWritePingMessage());
        const response = await receiveBinary(ws);

        expect(Array.from(response)).toEqual([RelayMessageType.Pong]);

        ws.close();
    });

    test("ignores oversized ping", async () => {
        const { ws } = await openSocket();

        ws.send(relayWritePingMessage(new Uint8Array(RelayMessagePingMaxSize)));
        await expectNoMessage(ws);

        ws.close();
    });

    test("ignores empty frames and keeps the connection open", async () => {
        const { ws } = await openSocket();

        ws.send(new Uint8Array(0));
        ws.send(relayWritePingMessage());
        const response = await receiveBinary(ws);

        expect(response[0]).toBe(RelayMessageType.Pong);
        expect(ws.readyState).toBe(WebSocket.OPEN);

        ws.close();
    });
});

describe("RelayServer backpressure", () => {
    const backpressurePort = 23146;
    const server = new RelayServer(backpressurePort, undefined, { backpressureLimitBytes: 64 * 1024 });

    beforeAll(() => {
        server.start();
    });

    afterAll(() => {
        server.stop();
    });

    test(
        "disconnects a subscriber that stops reading instead of buffering for it",
        async () => {
            const stalledSubscriber = await openStalledSubscriber(backpressurePort, "flood");
            const publisher = await openSocket(crypto.randomUUID(), backpressurePort);
            await waitFor(() => server.connectionCount === 2, 2000);
            expect(server.connectionCount).toBe(2);

            // Server and clients share this event loop, so yield regularly to let the relay run.
            const chunk = new Uint8Array(16 * 1024);
            const deadline = Date.now() + 15000;
            while (server.connectionCount === 2 && Date.now() < deadline) {
                for (let i = 0; i < 16; i++) sendChannelMessage(publisher.ws, publisher.clientGuid, "flood", chunk);
                await Bun.sleep(1);
            }

            expect(server.connectionCount).toBe(1);
            expect(publisher.ws.readyState).toBe(WebSocket.OPEN);

            stalledSubscriber.destroy();
            publisher.ws.close();
        },
        20000,
    );

    test("keeps a subscriber that reads everything", async () => {
        const publisher = await openSocket(crypto.randomUUID(), backpressurePort);
        const subscriber = await openSocket(crypto.randomUUID(), backpressurePort);
        await subscribeAndSettle(subscriber.ws, "steady");

        let receivedCount = 0;
        subscriber.ws.onmessage = () => receivedCount++;

        const chunk = new Uint8Array(1024);
        const messageCount = 2000;
        for (let i = 0; i < messageCount; i++) {
            sendChannelMessage(publisher.ws, publisher.clientGuid, "steady", chunk);
            if (i % 50 === 0) await Bun.sleep(1);
        }

        await waitFor(() => receivedCount === messageCount, 5000);
        expect(receivedCount).toBe(messageCount);
        expect(subscriber.ws.readyState).toBe(WebSocket.OPEN);

        publisher.ws.close();
        subscriber.ws.close();
    });
});
