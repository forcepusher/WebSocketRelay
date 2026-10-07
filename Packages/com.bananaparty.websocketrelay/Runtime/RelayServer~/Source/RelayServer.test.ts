import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import net from "node:net";
import { RelayCloseCode, RelayServer } from "./RelayServer";
import {
    RelayMessageType,
    RelayMessageChannelMessageChannelLengthOffset,
    RelayMessagePingMaxSize,
    RelayMessageSecretSize,
    relayReadGuid,
    relayReadChannel,
    relayReadChannelLength,
    relayChannelMessagePayloadOffset,
    relayWriteProtocolMessage,
    relayWriteChannelMessage,
    relayWriteHelloMessage,
    relayWritePingMessage,
} from "./RelayMessageType";

const testPort = 23145;

type TestClient = { ws: WebSocket; clientGuid: string; secret: Uint8Array };

function newSecret(): Uint8Array {
    return crypto.getRandomValues(new Uint8Array(RelayMessageSecretSize));
}

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

function waitForClose(ws: WebSocket, timeoutMs = 2000): Promise<CloseEvent> {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error("Timed out waiting for the connection to close")), timeoutMs);
        ws.onclose = (event) => {
            clearTimeout(timer);
            resolve(event);
        };
    });
}

async function connectSocket(port = testPort): Promise<WebSocket> {
    const ws = new WebSocket(`ws://127.0.0.1:${port}`);
    ws.binaryType = "arraybuffer";
    await new Promise<void>((resolve, reject) => {
        ws.onopen = () => resolve();
        ws.onerror = () => reject(new Error("WebSocket connection failed"));
    });
    return ws;
}

// Says hello and waits for a pong, which the relay sends only after it handled the hello.
async function openSocket(clientGuid = crypto.randomUUID(), port = testPort, secret = newSecret()): Promise<TestClient> {
    const ws = await connectSocket(port);
    ws.send(relayWriteHelloMessage(clientGuid, secret));
    ws.send(relayWritePingMessage());
    await receiveBinary(ws);
    return { ws, clientGuid, secret };
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

// A WebSocket client on a raw socket, so tests can stop reading or drop the connection without a close frame.
async function openRawClient(port: number, clientGuid: string, secret = newSecret()): Promise<net.Socket> {
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
    socket.write(maskedBinaryFrame(relayWriteHelloMessage(clientGuid, secret)));
    return socket;
}

// Subscribes and then stops reading, like a client on a stalled link.
async function openStalledSubscriber(port: number, channel: string): Promise<net.Socket> {
    const socket = await openRawClient(port, crypto.randomUUID());
    socket.write(maskedBinaryFrame(relayWriteProtocolMessage(RelayMessageType.Subscribe, channel)));
    return socket;
}

// Resumes a paused raw client and collects the payloads of the unmasked server frames it receives.
function readFramesUntil(socket: net.Socket, isLast: (payload: Uint8Array) => boolean, timeoutMs: number): Promise<Uint8Array[]> {
    return new Promise((resolve, reject) => {
        const payloads: Uint8Array[] = [];
        let pending = Buffer.alloc(0);
        const timer = setTimeout(() => {
            socket.off("data", onData);
            reject(new Error(`Timed out after ${payloads.length} frames`));
        }, timeoutMs);

        function onData(chunk: Buffer): void {
            pending = Buffer.concat([pending, chunk]);
            while (pending.length >= 2) {
                let length = pending[1]! & 0x7f;
                let offset = 2;
                if (length === 126) {
                    if (pending.length < 4) return;
                    length = pending.readUInt16BE(2);
                    offset = 4;
                } else if (length === 127) {
                    if (pending.length < 10) return;
                    length = Number(pending.readBigUInt64BE(2));
                    offset = 10;
                }
                if (pending.length < offset + length) return;

                const payload = new Uint8Array(pending.subarray(offset, offset + length));
                pending = pending.subarray(offset + length);
                payloads.push(payload);
                if (isLast(payload)) {
                    clearTimeout(timer);
                    socket.off("data", onData);
                    resolve(payloads);
                    return;
                }
            }
        }

        socket.on("data", onData);
        socket.resume();
    });
}

async function expectNoMessage(ws: WebSocket, timeoutMs = 100): Promise<void> {
    let unexpectedMessage = false;
    ws.onmessage = () => {
        unexpectedMessage = true;
    };

    await new Promise((resolve) => setTimeout(resolve, timeoutMs));
    expect(unexpectedMessage).toBe(false);
}

async function expectStaysOpen(ws: WebSocket, timeoutMs = 200): Promise<void> {
    let closed = false;
    ws.onclose = () => {
        closed = true;
    };

    await new Promise((resolve) => setTimeout(resolve, timeoutMs));
    expect(closed).toBe(false);
    expect(ws.readyState).toBe(WebSocket.OPEN);
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
        const ws = await connectSocket();
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

    test("relays channel messages with the sender guid", async () => {
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

    test("relays channel state like channel messages to receivers that keep up", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();
        await subscribeAndSettle(receiver.ws, "world");

        const state = relayWriteChannelMessage(sender.clientGuid, "world", new Uint8Array([5]), RelayMessageType.ChannelState);
        sender.ws.send(state);

        expect(await receiveBinary(receiver.ws)).toEqual(state);

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

    test("stops relaying to a client that unsubscribed", async () => {
        const sender = await openSocket();
        const receiver = await openSocket();
        await subscribeAndSettle(receiver.ws, "leave");
        unsubscribe(receiver.ws, "leave");
        await Bun.sleep(10);

        sendChannelMessage(sender.ws, sender.clientGuid, "leave", new Uint8Array([0x11]));
        await expectNoMessage(receiver.ws);

        sender.ws.close();
        receiver.ws.close();
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

    test("answers ping before hello", async () => {
        const ws = await connectSocket();

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

describe("RelayServer identities", () => {
    const identityPort = 23148;
    const reservationSeconds = 1;
    const server = new RelayServer(identityPort, undefined, { identityReservationSeconds: reservationSeconds });

    beforeAll(() => {
        server.start();
    });

    afterAll(() => {
        server.stop();
    });

    test("closes a connection that subscribes before saying hello", async () => {
        const ws = await connectSocket(identityPort);
        const closed = waitForClose(ws);

        subscribe(ws, "early");

        expect((await closed).code).toBe(RelayCloseCode.PolicyViolation);
    });

    test("closes a connection that says hello twice", async () => {
        const client = await openSocket(crypto.randomUUID(), identityPort);
        const closed = waitForClose(client.ws);

        client.ws.send(relayWriteHelloMessage(crypto.randomUUID(), newSecret()));

        expect((await closed).code).toBe(RelayCloseCode.PolicyViolation);
    });

    test("closes a connection that sends as another client", async () => {
        const spoofer = await openSocket(crypto.randomUUID(), identityPort);
        const victim = await openSocket(crypto.randomUUID(), identityPort);
        await subscribeAndSettle(victim.ws, "spoof");

        const closed = waitForClose(spoofer.ws);
        const victimSilence = expectNoMessage(victim.ws, 200);
        sendChannelMessage(spoofer.ws, victim.clientGuid, "spoof", new Uint8Array([1]));

        expect((await closed).code).toBe(RelayCloseCode.PolicyViolation);
        await victimSilence;

        victim.ws.close();
    });

    test("rejects a guid that another client holds", async () => {
        const owner = await openSocket(crypto.randomUUID(), identityPort);
        const impostor = await connectSocket(identityPort);
        const closed = waitForClose(impostor);

        impostor.send(relayWriteHelloMessage(owner.clientGuid, newSecret()));

        expect((await closed).code).toBe(RelayCloseCode.IdentityTaken);
        await expectStaysOpen(owner.ws);

        owner.ws.close();
    });

    test("replaces the older connection of a client that connects again", async () => {
        const first = await openSocket(crypto.randomUUID(), identityPort);
        const receiver = await openSocket(crypto.randomUUID(), identityPort);
        await subscribeAndSettle(first.ws, "resume");
        await subscribeAndSettle(receiver.ws, "resume");

        const firstClosed = waitForClose(first.ws);
        const second = await openSocket(first.clientGuid, identityPort, first.secret);
        expect((await firstClosed).code).toBe(RelayCloseCode.Replaced);

        sendChannelMessage(second.ws, second.clientGuid, "resume", new Uint8Array([7]));
        const response = await receiveBinary(receiver.ws);
        expect(relayReadGuid(response, 1)).toBe(first.clientGuid);

        second.ws.close();
        receiver.ws.close();
    });

    test("keeps the guid of a dropped connection for its client", async () => {
        const clientGuid = crypto.randomUUID();
        const secret = newSecret();
        const dropped = await openRawClient(identityPort, clientGuid, secret);
        await Bun.sleep(50);
        dropped.destroy();
        await Bun.sleep(100);

        const impostor = await connectSocket(identityPort);
        const impostorClosed = waitForClose(impostor);
        impostor.send(relayWriteHelloMessage(clientGuid, newSecret()));
        expect((await impostorClosed).code).toBe(RelayCloseCode.IdentityTaken);

        const returning = await openSocket(clientGuid, identityPort, secret);
        await expectStaysOpen(returning.ws);

        returning.ws.close();
    });

    test("releases the guid of a dropped connection after the reservation", async () => {
        const clientGuid = crypto.randomUUID();
        const dropped = await openRawClient(identityPort, clientGuid);
        await Bun.sleep(50);
        dropped.destroy();
        await Bun.sleep(reservationSeconds * 1000 + 500);

        const next = await openSocket(clientGuid, identityPort);
        await expectStaysOpen(next.ws);

        next.ws.close();
    });

    test("releases the guid when its client closes the connection", async () => {
        const first = await openSocket(crypto.randomUUID(), identityPort);
        const firstClosed = waitForClose(first.ws);
        first.ws.close();
        await firstClosed;
        await Bun.sleep(50);

        const next = await openSocket(first.clientGuid, identityPort);
        await expectStaysOpen(next.ws);

        next.ws.close();
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

describe("RelayServer channel state", () => {
    const statePort = 23149;
    const server = new RelayServer(statePort, undefined, { stateSkipBufferedBytes: 16 * 1024 });

    beforeAll(() => {
        server.start();
    });

    afterAll(() => {
        server.stop();
    });

    test(
        "skips channel state for a receiver that fell behind but still delivers channel messages",
        async () => {
            const stalledSubscriber = await openStalledSubscriber(statePort, "state");
            const publisher = await openSocket(crypto.randomUUID(), statePort);
            await waitFor(() => server.connectionCount === 2, 2000);

            const state = relayWriteChannelMessage(
                publisher.clientGuid,
                "state",
                new Uint8Array(16 * 1024),
                RelayMessageType.ChannelState,
            );
            const deadline = Date.now() + 15000;
            while (server.skippedStateCount === 0 && Date.now() < deadline) {
                for (let i = 0; i < 16; i++) publisher.ws.send(state);
                await Bun.sleep(1);
            }
            expect(server.skippedStateCount).toBeGreaterThan(0);

            sendChannelMessage(publisher.ws, publisher.clientGuid, "state", new Uint8Array([0xde, 0xad]));
            const frames = await readFramesUntil(
                stalledSubscriber,
                (payload) => payload[0] === RelayMessageType.ChannelMessage,
                15000,
            );

            expect(frames.at(-1)![0]).toBe(RelayMessageType.ChannelMessage);
            expect(frames.slice(0, -1).every((payload) => payload[0] === RelayMessageType.ChannelState)).toBe(true);

            stalledSubscriber.destroy();
            publisher.ws.close();
        },
        40000,
    );
});
