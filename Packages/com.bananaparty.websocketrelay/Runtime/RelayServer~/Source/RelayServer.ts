import { timingSafeEqual } from "node:crypto";
import {
    RelayMessageType,
    relayMessageTypeName,
    RelayMessageChannelMessageChannelLengthOffset,
    RelayMessageChannelMessageGuidOffset,
    RelayMessageGuidSize,
    RelayMessageHelloSize,
    RelayMessagePingMaxSize,
    relayReadGuid,
    relayReadChannel,
    relayReadChannelLength,
} from "./RelayMessageType";
import { RelayServerLog } from "./RelayServerLog";

type RelayWebSocket = Bun.ServerWebSocket<RelayWebSocketData>;

type RelayWebSocketData = {
    connectionId: number;
    /** The client guid from the hello message. Every channel message from this connection must carry it. */
    guid?: string;
    guidBytes?: Uint8Array;
    channels: Set<string>;
};

/** A client guid, the secret its client proved it with, and the connection that holds it. */
type RelayIdentity = {
    secret: Uint8Array;
    connection: RelayWebSocket | null;
    /** When the connection dropped without a close frame, so the guid stays reserved for a while. */
    droppedAt: number;
};

type RelayServerTlsOptions = {
    cert: string;
    key: string;
};

export type RelayServerOptions = {
    /** Seconds without any traffic before a connection is dropped. Clients heartbeat every second. */
    idleTimeoutSeconds?: number;
    /** Bytes queued for a slow client before it is disconnected instead of buffering stale messages. */
    backpressureLimitBytes?: number;
    /** Bytes queued for a client above which channel state is skipped for it, because the next state replaces it. */
    stateSkipBufferedBytes?: number;
    /** Seconds a client guid stays reserved after its connection dropped, so only that client can reconnect with it. */
    identityReservationSeconds?: number;
};

// Clients heartbeat every second, so only a dead socket stays quiet this long.
export const RelayServerDefaultIdleTimeoutSeconds = 60;
// A short stall fits under this. A client further behind is disconnected and resynchronizes on reconnect.
export const RelayServerDefaultBackpressureLimitBytes = 4 * 1024 * 1024;
export const RelayServerDefaultStateSkipBufferedBytes = 16 * 1024;
// Longer than clients keep trying to reconnect, 30 s by default.
export const RelayServerDefaultIdentityReservationSeconds = 60;

export const RelayCloseCode = {
    /** Protocol misuse: no hello first, a second hello, or a channel message carrying another client's guid. */
    PolicyViolation: 1008,
    /** Another client holds the guid. */
    IdentityTaken: 4001,
    /** The same client connected again, so this older connection is closed. */
    Replaced: 4002,
} as const;

// Reported for connections that ended without a close frame, whose client may still come back.
const abnormalClosureCode = 1006;

const sweepIntervalMs = 250;

// Bun silently drops messages to a client once its own backpressure limit is reached
// and keeps the connection open. Its limit is kept above ours so that slow clients are
// disconnected and resynchronize on reconnect instead of receiving a stream with gaps.
const bunBackpressureLimitMultiplier = 4;

export class RelayServer {
    #port: number;
    #tls?: RelayServerTlsOptions;
    #idleTimeoutSeconds: number;
    #backpressureLimitBytes: number;
    #stateSkipBufferedBytes: number;
    #identityReservationMs: number;
    #server: Bun.Server<RelayWebSocketData> | null = null;
    #sweepTimer: ReturnType<typeof setInterval> | null = null;
    #connections = new Set<RelayWebSocket>();
    #channels = new Map<string, Set<RelayWebSocket>>();
    #identities = new Map<string, RelayIdentity>();
    #nextConnectionId = 1;
    #skippedStateCount = 0;

    constructor(port: number = 80, tls?: RelayServerTlsOptions, options: RelayServerOptions = {}) {
        this.#port = port;
        this.#tls = tls;
        this.#idleTimeoutSeconds = options.idleTimeoutSeconds ?? RelayServerDefaultIdleTimeoutSeconds;
        this.#backpressureLimitBytes = options.backpressureLimitBytes ?? RelayServerDefaultBackpressureLimitBytes;
        this.#stateSkipBufferedBytes = options.stateSkipBufferedBytes ?? RelayServerDefaultStateSkipBufferedBytes;
        this.#identityReservationMs =
            (options.identityReservationSeconds ?? RelayServerDefaultIdentityReservationSeconds) * 1000;
    }

    get connectionCount(): number {
        return this.#connections.size;
    }

    /** Channel state messages not sent to receivers that were behind. */
    get skippedStateCount(): number {
        return this.#skippedStateCount;
    }

    start(): void {
        this.#server = Bun.serve<RelayWebSocketData>({
            port: this.#port,
            ...(this.#tls
                ? {
                      tls: {
                          cert: Bun.file(this.#tls.cert),
                          key: Bun.file(this.#tls.key),
                      },
                  }
                : {}),
            fetch: (req, server) => {
                const connectionId = this.#nextConnectionId;
                if (server.upgrade(req, { data: { connectionId, channels: new Set<string>() } })) {
                    this.#nextConnectionId++;
                    RelayServerLog.debug(
                        `upgrade requested id=${connectionId} remote=${server.requestIP(req)?.address ?? "unknown"}`,
                    );
                    return undefined;
                }

                return new Response("WebSocket Relay Server");
            },
            websocket: {
                data: {} as RelayWebSocketData,
                idleTimeout: this.#idleTimeoutSeconds,
                backpressureLimit: this.#backpressureLimitBytes * bunBackpressureLimitMultiplier,
                open: (ws) => {
                    this.#connections.add(ws);
                    RelayServerLog.info(`connected id=${ws.data.connectionId} remote=${ws.remoteAddress}`);
                },
                close: (ws, code, reason) => {
                    this.#connections.delete(ws);
                    const channels = [...ws.data.channels].join(", ");
                    this.#leaveAllChannels(ws);
                    this.#releaseIdentity(ws, code);
                    RelayServerLog.info(
                        `disconnected id=${ws.data.connectionId} remote=${ws.remoteAddress} guid=${ws.data.guid ?? "none"} code=${code} reason=${reason || "none"} subscriptions=[${channels}]`,
                    );
                },
                message: (ws, message) => {
                    if (!(message instanceof Uint8Array)) {
                        RelayServerLog.warn(
                            `ignored non-binary frame id=${ws.data.connectionId} type=${typeof message}`,
                        );
                        return;
                    }

                    if (message.byteLength === 0) {
                        RelayServerLog.warn(`ignored empty frame id=${ws.data.connectionId}`);
                        return;
                    }

                    const type = message[0];

                    // Heartbeats arrive every second per client and would drown out the debug log.
                    if (type === RelayMessageType.Ping) {
                        this.#handlePing(ws, message);
                        return;
                    }

                    RelayServerLog.debug(
                        `message id=${ws.data.connectionId} type=${relayMessageTypeName(type)} bytes=${message.byteLength}`,
                    );

                    if (type === RelayMessageType.Hello) {
                        this.#handleHello(ws, message);
                        return;
                    }

                    if (ws.data.guid === undefined) {
                        this.#closeForPolicyViolation(ws, `${relayMessageTypeName(type)}-before-hello`);
                        return;
                    }

                    switch (type) {
                        case RelayMessageType.Subscribe:
                            this.#handleSubscribe(ws, message);
                            break;
                        case RelayMessageType.Unsubscribe:
                            this.#handleUnsubscribe(ws, message);
                            break;
                        case RelayMessageType.ChannelMessage:
                            this.#handleChannelMessage(ws, message, false);
                            break;
                        case RelayMessageType.ChannelState:
                            this.#handleChannelMessage(ws, message, true);
                            break;
                        default:
                            RelayServerLog.warn(
                                `unknown message id=${ws.data.connectionId} type=${relayMessageTypeName(type)}`,
                            );
                            break;
                    }
                },
            },
        });

        this.#sweepTimer = setInterval(() => {
            this.#disconnectSlowClients();
            this.#expireIdentities();
        }, sweepIntervalMs);

        const scheme = this.#tls ? "wss" : "ws";
        RelayServerLog.info(
            `listening on ${scheme}://0.0.0.0:${this.#port} idleTimeout=${this.#idleTimeoutSeconds}s backpressureLimit=${this.#backpressureLimitBytes}B debug=${process.env.RELAY_DEBUG === "1" ? "verbose" : "basic"}`,
        );
    }

    stop(): void {
        if (this.#sweepTimer) {
            clearInterval(this.#sweepTimer);
            this.#sweepTimer = null;
        }

        if (this.#server) {
            this.#server.stop();
            this.#server = null;
            RelayServerLog.info("stopped");
        }
    }

    #disconnectSlowClients(): void {
        for (const ws of this.#connections) {
            const bufferedBytes = ws.getBufferedAmount();
            if (bufferedBytes <= this.#backpressureLimitBytes) continue;

            RelayServerLog.warn(
                `disconnecting slow client id=${ws.data.connectionId} remote=${ws.remoteAddress} guid=${ws.data.guid ?? "none"} bufferedBytes=${bufferedBytes}`,
            );
            // terminate() does not wait for a close handshake that a stalled client cannot complete.
            ws.terminate();
        }
    }

    #expireIdentities(): void {
        const droppedBefore = Date.now() - this.#identityReservationMs;
        for (const [guid, identity] of this.#identities) {
            if (identity.connection === null && identity.droppedAt <= droppedBefore) this.#identities.delete(guid);
        }
    }

    #closeForPolicyViolation(ws: RelayWebSocket, reason: string): void {
        RelayServerLog.warn(
            `closing id=${ws.data.connectionId} remote=${ws.remoteAddress} guid=${ws.data.guid ?? "none"} reason=${reason}`,
        );
        ws.close(RelayCloseCode.PolicyViolation, reason);
    }

    #handlePing(ws: RelayWebSocket, message: Uint8Array): void {
        if (message.byteLength > RelayMessagePingMaxSize) {
            RelayServerLog.warn(
                `ping rejected id=${ws.data.connectionId} reason=oversized bytes=${message.byteLength}`,
            );
            return;
        }

        const pong = new Uint8Array(message);
        pong[0] = RelayMessageType.Pong;
        ws.send(pong);
    }

    #handleHello(ws: RelayWebSocket, message: Uint8Array): void {
        if (ws.data.guid !== undefined) {
            this.#closeForPolicyViolation(ws, "second-hello");
            return;
        }

        if (message.byteLength !== RelayMessageHelloSize) {
            this.#closeForPolicyViolation(ws, "malformed-hello");
            return;
        }

        const guid = relayReadGuid(message, 1);
        const secret = message.slice(1 + RelayMessageGuidSize);
        const identity = this.#identities.get(guid);

        if (identity && !timingSafeEqual(identity.secret, secret)) {
            RelayServerLog.warn(
                `hello rejected id=${ws.data.connectionId} remote=${ws.remoteAddress} guid=${guid} reason=guid-taken`,
            );
            ws.close(RelayCloseCode.IdentityTaken, "Client guid is taken");
            return;
        }

        const previous = identity?.connection;
        if (previous) {
            // The client reconnected before its old connection was noticed as gone,
            // so the old one stops receiving messages for a client that no longer reads them.
            this.#leaveAllChannels(previous);
            RelayServerLog.info(
                `replacing id=${previous.data.connectionId} with id=${ws.data.connectionId} guid=${guid}`,
            );
            previous.close(RelayCloseCode.Replaced, "Replaced by a newer connection");
        }

        this.#identities.set(guid, { secret, connection: ws, droppedAt: 0 });
        ws.data.guid = guid;
        ws.data.guidBytes = message.slice(1, 1 + RelayMessageGuidSize);
        RelayServerLog.info(`identified id=${ws.data.connectionId} guid=${guid}`);
    }

    #releaseIdentity(ws: RelayWebSocket, closeCode: number): void {
        const guid = ws.data.guid;
        if (guid === undefined) return;

        const identity = this.#identities.get(guid);
        if (identity?.connection !== ws) return;

        // A client that closed the connection itself is done with the guid.
        // One that dropped may reconnect, so the guid stays reserved for it.
        if (closeCode !== abnormalClosureCode) {
            this.#identities.delete(guid);
            return;
        }

        identity.connection = null;
        identity.droppedAt = Date.now();
    }

    #handleSubscribe(ws: RelayWebSocket, message: Uint8Array): void {
        const channel = relayReadChannel(message);
        if (!channel) {
            RelayServerLog.warn(
                `subscribe rejected id=${ws.data.connectionId} reason=missing-channel bytes=${message.byteLength}`,
            );
            return;
        }

        if (ws.data.channels.has(channel)) {
            RelayServerLog.debug(
                `subscribe ignored id=${ws.data.connectionId} channel=${channel} reason=already-subscribed`,
            );
            return;
        }

        ws.data.channels.add(channel);
        let subscribers = this.#channels.get(channel);
        if (!subscribers) {
            subscribers = new Set();
            this.#channels.set(channel, subscribers);
        }
        subscribers.add(ws);

        RelayServerLog.info(
            `subscribed id=${ws.data.connectionId} channel=${channel} subscriptions=[${[...ws.data.channels].join(", ")}]`,
        );
    }

    #handleUnsubscribe(ws: RelayWebSocket, message: Uint8Array): void {
        const channel = relayReadChannel(message);
        if (!channel) {
            RelayServerLog.warn(
                `unsubscribe rejected id=${ws.data.connectionId} reason=missing-channel bytes=${message.byteLength}`,
            );
            return;
        }

        if (!ws.data.channels.has(channel)) {
            RelayServerLog.debug(
                `unsubscribe ignored id=${ws.data.connectionId} channel=${channel} reason=not-subscribed`,
            );
            return;
        }

        this.#leaveChannel(ws, channel);

        RelayServerLog.info(
            `unsubscribed id=${ws.data.connectionId} channel=${channel} subscriptions=[${[...ws.data.channels].join(", ")}]`,
        );
    }

    #leaveChannel(ws: RelayWebSocket, channel: string): void {
        ws.data.channels.delete(channel);
        const subscribers = this.#channels.get(channel);
        if (!subscribers) return;

        subscribers.delete(ws);
        if (subscribers.size === 0) this.#channels.delete(channel);
    }

    #leaveAllChannels(ws: RelayWebSocket): void {
        for (const channel of [...ws.data.channels]) this.#leaveChannel(ws, channel);
    }

    #handleChannelMessage(ws: RelayWebSocket, message: Uint8Array, isState: boolean): void {
        const channelLength = relayReadChannelLength(message, RelayMessageChannelMessageChannelLengthOffset);
        if (channelLength < 0) {
            RelayServerLog.warn(
                `channel message rejected id=${ws.data.connectionId} reason=short-frame bytes=${message.byteLength}`,
            );
            return;
        }

        const channel = relayReadChannel(message, RelayMessageChannelMessageChannelLengthOffset);
        if (!channel) {
            RelayServerLog.warn(
                `channel message rejected id=${ws.data.connectionId} reason=missing-channel bytes=${message.byteLength}`,
            );
            return;
        }

        // Receivers trust the sender guid, so it has to be the one this connection said hello with.
        if (!hasBytesAt(message, RelayMessageChannelMessageGuidOffset, ws.data.guidBytes!)) {
            this.#closeForPolicyViolation(ws, `sender-guid-mismatch claimed=${relayReadGuid(message, 1)}`);
            return;
        }

        let deliveredCount = 0;
        let skippedCount = 0;
        for (const subscriber of this.#channels.get(channel) ?? []) {
            if (subscriber === ws) continue;

            // A receiver this far behind would get stale state, and the next state replaces it anyway.
            if (isState && subscriber.getBufferedAmount() > this.#stateSkipBufferedBytes) {
                skippedCount++;
                continue;
            }

            subscriber.send(message);
            deliveredCount++;
        }

        this.#skippedStateCount += skippedCount;
        RelayServerLog.debug(
            `published id=${ws.data.connectionId} guid=${ws.data.guid} channel=${channel} type=${relayMessageTypeName(message[0]!)} bytes=${message.byteLength} delivered=${deliveredCount} skipped=${skippedCount}`,
        );
    }
}

function hasBytesAt(message: Uint8Array, offset: number, expected: Uint8Array): boolean {
    if (message.byteLength < offset + expected.byteLength) return false;

    for (let index = 0; index < expected.byteLength; index++) {
        if (message[offset + index] !== expected[index]) return false;
    }

    return true;
}
