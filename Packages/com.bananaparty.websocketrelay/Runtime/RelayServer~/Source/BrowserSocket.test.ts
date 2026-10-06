import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { existsSync, readFileSync } from "node:fs";
import net from "node:net";
import { fileURLToPath } from "node:url";
import { RelayServer } from "./RelayServer";
import {
    RelayMessageType,
    relayWriteChannelMessage,
    relayWritePingMessage,
    relayWriteProtocolMessage,
} from "./RelayMessageType";

const testPort = 23147;
const abnormalClosureCode = 1006;
const unreadLimitClosureCode = 1009;
const jslibPath = fileURLToPath(new URL("../../Socket/BrowserSocket.jslib", import.meta.url));

type BrowserSocketExports = {
    GetBrowserSocketIsConnected(socketId: number): boolean;
    GetBrowserSocketCloseCode(socketId: number): number;
    GetBrowserSocketBufferedAmount(socketId: number): number;
    GetBrowserSocketHasUnreadPayloadQueue(socketId: number): boolean;
    BrowserSocketReadPayloadQueue(socketId: number, bufferPtr: number, bufferLength: number): number;
    BrowserSocketConnect(serverAddressPtr: number): number;
    BrowserSocketSend(socketId: number, payloadPtr: number, payloadLength: number): void;
    BrowserSocketDisconnect(socketId: number): void;
    BrowserSocketDispose(socketId: number): void;
    $browserSocket: { sockets: Record<number, unknown>; maxUnreadPayloadBytes: number };
};

// Links the jslib the way Emscripten does: $-prefixed members become module variables,
// and exported functions reach C# memory through HEAPU8 and UTF8ToString.
class EmscriptenHarness {
    readonly heap = new Uint8Array(1 << 20);
    readonly exports: BrowserSocketExports;
    readonly #strings = new Map<number, string>();
    #nextStringPtr = 1;
    #nextHeapOffset = 0;

    constructor(source: string) {
        const library: Record<string, unknown> = {};
        const link = new Function(
            "LibraryManager",
            "mergeInto",
            "autoAddDeps",
            "HEAPU8",
            "UTF8ToString",
            `${source}\nvar browserSocket = LibraryManager.library.$browserSocket;`,
        );
        link(
            { library },
            (target: object, members: object) => Object.assign(target, members),
            () => {},
            this.heap,
            (ptr: number) => this.#strings.get(ptr),
        );
        this.exports = library as unknown as BrowserSocketExports;
    }

    connect(serverAddress = `ws://127.0.0.1:${testPort}`): number {
        const ptr = this.#nextStringPtr++;
        this.#strings.set(ptr, serverAddress);
        return this.exports.BrowserSocketConnect(ptr);
    }

    send(socketId: number, payload: Uint8Array): void {
        const ptr = this.#allocate(payload.byteLength);
        this.heap.set(payload, ptr);
        this.exports.BrowserSocketSend(socketId, ptr, payload.byteLength);
    }

    // Mirrors BrowserSocket.ReadPayloadQueue: ask for the size first, then read into a buffer that fits.
    readPayload(socketId: number): Uint8Array {
        const payloadSize = this.exports.BrowserSocketReadPayloadQueue(socketId, 0, 0);
        const ptr = this.#allocate(payloadSize);
        expect(this.exports.BrowserSocketReadPayloadQueue(socketId, ptr, payloadSize)).toBe(payloadSize);
        return this.heap.slice(ptr, ptr + payloadSize);
    }

    #allocate(size: number): number {
        if (this.#nextHeapOffset + size > this.heap.byteLength) this.#nextHeapOffset = 0;
        const ptr = this.#nextHeapOffset;
        this.#nextHeapOffset += size;
        return ptr;
    }
}

async function waitFor(condition: () => boolean, timeoutMs = 2000): Promise<void> {
    const deadline = Date.now() + timeoutMs;
    while (!condition() && Date.now() < deadline) await Bun.sleep(5);
}

describe.skipIf(!existsSync(jslibPath))("BrowserSocket.jslib", () => {
    let server: RelayServer;
    let harness: EmscriptenHarness;

    async function connectOpen(): Promise<number> {
        const socketId = harness.connect();
        await waitFor(() => harness.exports.GetBrowserSocketIsConnected(socketId));
        expect(harness.exports.GetBrowserSocketIsConnected(socketId)).toBe(true);
        return socketId;
    }

    beforeAll(() => {
        server = new RelayServer(testPort);
        server.start();
        harness = new EmscriptenHarness(readFileSync(jslibPath, "utf8"));
    });

    afterAll(() => {
        server.stop();
    });

    test("relays channel messages between sockets", async () => {
        const sender = await connectOpen();
        const receiver = await connectOpen();
        harness.send(receiver, relayWriteProtocolMessage(RelayMessageType.Subscribe, "jslib-relay"));
        await Bun.sleep(20);

        const message = relayWriteChannelMessage(crypto.randomUUID(), "jslib-relay", new Uint8Array([1, 2, 3]));
        harness.send(sender, message);
        await waitFor(() => harness.exports.GetBrowserSocketHasUnreadPayloadQueue(receiver));

        expect(harness.readPayload(receiver)).toEqual(message);
        expect(harness.exports.GetBrowserSocketHasUnreadPayloadQueue(receiver)).toBe(false);

        harness.exports.BrowserSocketDispose(sender);
        harness.exports.BrowserSocketDispose(receiver);
    });

    test("receives pong for ping", async () => {
        const socketId = await connectOpen();
        const ping = relayWritePingMessage(new Uint8Array([9, 8, 7, 6, 5, 4, 3, 2]));

        harness.send(socketId, ping);
        await waitFor(() => harness.exports.GetBrowserSocketHasUnreadPayloadQueue(socketId));

        const pong = harness.readPayload(socketId);
        expect(pong[0]).toBe(RelayMessageType.Pong);
        expect(pong.slice(1)).toEqual(ping.slice(1));

        harness.exports.BrowserSocketDispose(socketId);
    });

    test("keeps a payload queued when the buffer is too small", async () => {
        const socketId = await connectOpen();
        harness.send(socketId, relayWritePingMessage(new Uint8Array(8)));
        await waitFor(() => harness.exports.GetBrowserSocketHasUnreadPayloadQueue(socketId));

        expect(harness.exports.BrowserSocketReadPayloadQueue(socketId, 0, 4)).toBe(9);
        expect(harness.exports.GetBrowserSocketHasUnreadPayloadQueue(socketId)).toBe(true);
        expect(harness.readPayload(socketId).byteLength).toBe(9);

        harness.exports.BrowserSocketDispose(socketId);
    });

    test("reports buffered amount of an open socket", async () => {
        const socketId = await connectOpen();
        expect(harness.exports.GetBrowserSocketBufferedAmount(socketId)).toBeGreaterThanOrEqual(0);
        harness.exports.BrowserSocketDispose(socketId);
    });

    test("is not closed while connecting or open", async () => {
        const socketId = harness.connect();
        expect(harness.exports.GetBrowserSocketCloseCode(socketId)).toBe(0);

        await waitFor(() => harness.exports.GetBrowserSocketIsConnected(socketId));
        expect(harness.exports.GetBrowserSocketCloseCode(socketId)).toBe(0);

        harness.exports.BrowserSocketDispose(socketId);
    });

    test("reports close code after disconnecting", async () => {
        const socketId = await connectOpen();

        harness.exports.BrowserSocketDisconnect(socketId);
        await waitFor(() => harness.exports.GetBrowserSocketCloseCode(socketId) !== 0);

        expect(harness.exports.GetBrowserSocketCloseCode(socketId)).not.toBe(0);
        expect(harness.exports.GetBrowserSocketIsConnected(socketId)).toBe(false);
        harness.exports.BrowserSocketDisconnect(socketId);
        harness.exports.BrowserSocketDispose(socketId);
    });

    test("reports abnormal closure when the server drops the handshake", async () => {
        const droppingServer = net.createServer((connection) => connection.destroy());
        await new Promise<void>((resolve) => droppingServer.listen(0, "127.0.0.1", resolve));
        const { port } = droppingServer.address() as net.AddressInfo;

        try {
            const socketId = harness.connect(`ws://127.0.0.1:${port}`);
            await waitFor(() => harness.exports.GetBrowserSocketCloseCode(socketId) !== 0, 5000);

            expect(harness.exports.GetBrowserSocketCloseCode(socketId)).toBe(abnormalClosureCode);
            expect(harness.exports.GetBrowserSocketIsConnected(socketId)).toBe(false);
            harness.exports.BrowserSocketDispose(socketId);
        } finally {
            droppingServer.close();
        }
    });

    test("reports abnormal closure for an invalid address without throwing", () => {
        const socketId = harness.connect("not a websocket address");

        expect(harness.exports.GetBrowserSocketCloseCode(socketId)).toBe(abnormalClosureCode);
        expect(harness.exports.GetBrowserSocketIsConnected(socketId)).toBe(false);
        expect(harness.exports.GetBrowserSocketBufferedAmount(socketId)).toBe(0);
        harness.send(socketId, new Uint8Array([1]));
        harness.exports.BrowserSocketDisconnect(socketId);
        harness.exports.BrowserSocketDispose(socketId);
    });

    test("drops sends while connecting instead of throwing", () => {
        const socketId = harness.connect();
        harness.send(socketId, relayWritePingMessage());
        harness.exports.BrowserSocketDispose(socketId);
    });

    test("drops the connection when received data is not read", async () => {
        const socketId = await connectOpen();
        const previousLimit = harness.exports.$browserSocket.maxUnreadPayloadBytes;
        harness.exports.$browserSocket.maxUnreadPayloadBytes = 20;
        try {
            for (let index = 0; index < 3; index++) harness.send(socketId, relayWritePingMessage(new Uint8Array(8)));
            await waitFor(() => harness.exports.GetBrowserSocketCloseCode(socketId) !== 0);

            expect(harness.exports.GetBrowserSocketCloseCode(socketId)).toBe(unreadLimitClosureCode);
            expect(harness.exports.GetBrowserSocketIsConnected(socketId)).toBe(false);
            expect(harness.exports.GetBrowserSocketHasUnreadPayloadQueue(socketId)).toBe(false);
        } finally {
            harness.exports.$browserSocket.maxUnreadPayloadBytes = previousLimit;
            harness.exports.BrowserSocketDispose(socketId);
        }
    });

    test("keeps the connection while received data is read", async () => {
        const socketId = await connectOpen();
        const previousLimit = harness.exports.$browserSocket.maxUnreadPayloadBytes;
        harness.exports.$browserSocket.maxUnreadPayloadBytes = 20;
        try {
            for (let index = 0; index < 5; index++) {
                harness.send(socketId, relayWritePingMessage(new Uint8Array(8)));
                await waitFor(() => harness.exports.GetBrowserSocketHasUnreadPayloadQueue(socketId));
                expect(harness.readPayload(socketId)[0]).toBe(RelayMessageType.Pong);
            }

            expect(harness.exports.GetBrowserSocketIsConnected(socketId)).toBe(true);
            expect(harness.exports.GetBrowserSocketCloseCode(socketId)).toBe(0);
        } finally {
            harness.exports.$browserSocket.maxUnreadPayloadBytes = previousLimit;
            harness.exports.BrowserSocketDispose(socketId);
        }
    });

    test("dispose frees the socket and later calls are harmless", async () => {
        const socketId = await connectOpen();
        harness.exports.BrowserSocketDispose(socketId);

        expect(harness.exports.$browserSocket.sockets[socketId]).toBeUndefined();
        expect(harness.exports.GetBrowserSocketIsConnected(socketId)).toBe(false);
        expect(harness.exports.GetBrowserSocketCloseCode(socketId)).toBe(abnormalClosureCode);
        expect(harness.exports.GetBrowserSocketBufferedAmount(socketId)).toBe(0);
        expect(harness.exports.GetBrowserSocketHasUnreadPayloadQueue(socketId)).toBe(false);
        harness.send(socketId, new Uint8Array([1]));
        harness.exports.BrowserSocketDisconnect(socketId);
        harness.exports.BrowserSocketDispose(socketId);
    });

    test("never reuses ids of disposed sockets", () => {
        const firstSocketId = harness.connect();
        harness.exports.BrowserSocketDispose(firstSocketId);
        const secondSocketId = harness.connect();

        expect(secondSocketId).not.toBe(firstSocketId);
        expect(secondSocketId).not.toBe(0);
        harness.exports.BrowserSocketDispose(secondSocketId);
    });
});
