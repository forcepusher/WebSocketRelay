import {
    RelayMessageType,
    relayMessageTypeName,
    RelayMessageChannelMessageChannelLengthOffset,
    RelayMessagePingMaxSize,
    relayReadGuid,
    relayReadChannel,
    relayReadChannelLength,
} from "./RelayMessageType";
import { RelayServerLog } from "./RelayServerLog";

type RelayWebSocketData = {
    connectionId: number;
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
};

export const RelayServerDefaultIdleTimeoutSeconds = 32;
export const RelayServerDefaultBackpressureLimitBytes = 1024 * 1024;

const backpressureSweepIntervalMs = 250;

// Bun silently drops messages to a client once its own backpressure limit is reached
// and keeps the connection open. Its limit is kept above ours so that slow clients are
// disconnected and resynchronize on reconnect instead of receiving a stream with gaps.
const bunBackpressureLimitMultiplier = 4;

export class RelayServer {
    #port: number;
    #tls?: RelayServerTlsOptions;
    #idleTimeoutSeconds: number;
    #backpressureLimitBytes: number;
    #server: Bun.Server<RelayWebSocketData> | null = null;
    #backpressureSweepTimer: ReturnType<typeof setInterval> | null = null;
    #connections = new Set<Bun.ServerWebSocket<RelayWebSocketData>>();
    #nextConnectionId = 1;

    constructor(port: number = 80, tls?: RelayServerTlsOptions, options: RelayServerOptions = {}) {
        this.#port = port;
        this.#tls = tls;
        this.#idleTimeoutSeconds = options.idleTimeoutSeconds ?? RelayServerDefaultIdleTimeoutSeconds;
        this.#backpressureLimitBytes = options.backpressureLimitBytes ?? RelayServerDefaultBackpressureLimitBytes;
    }

    get connectionCount(): number {
        return this.#connections.size;
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
                const connectionId = this.#nextConnectionId++;
                if (
                    server.upgrade(req, {
                        data: { connectionId },
                    })
                ) {
                    RelayServerLog.debug(
                        `upgrade requested connectionId=${connectionId} remote=${req.headers.get("host") ?? "unknown"}`,
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
                    RelayServerLog.info(
                        `connected id=${ws.data.connectionId} remote=${ws.remoteAddress} subscriptions=[]`,
                    );
                },
                close: (ws, code, reason) => {
                    this.#connections.delete(ws);
                    RelayServerLog.info(
                        `disconnected id=${ws.data.connectionId} remote=${ws.remoteAddress} code=${code} reason=${reason || "none"} subscriptions=[${ws.subscriptions.join(", ")}]`,
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

                    switch (type) {
                        case RelayMessageType.Subscribe:
                            this.#handleSubscribe(ws, message);
                            break;
                        case RelayMessageType.Unsubscribe:
                            this.#handleUnsubscribe(ws, message);
                            break;
                        case RelayMessageType.ChannelMessage:
                            this.#handleChannelMessage(ws, message);
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

        this.#backpressureSweepTimer = setInterval(() => this.#disconnectSlowClients(), backpressureSweepIntervalMs);

        const scheme = this.#tls ? "wss" : "ws";
        RelayServerLog.info(
            `listening on ${scheme}://0.0.0.0:${this.#port} idleTimeout=${this.#idleTimeoutSeconds}s backpressureLimit=${this.#backpressureLimitBytes}B debug=${process.env.RELAY_DEBUG === "1" ? "verbose" : "basic"}`,
        );
    }

    stop(): void {
        if (this.#backpressureSweepTimer) {
            clearInterval(this.#backpressureSweepTimer);
            this.#backpressureSweepTimer = null;
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
                `disconnecting slow client id=${ws.data.connectionId} remote=${ws.remoteAddress} bufferedBytes=${bufferedBytes}`,
            );
            // terminate() does not wait for a close handshake that a stalled client cannot complete.
            ws.terminate();
        }
    }

    #handlePing(ws: Bun.ServerWebSocket<RelayWebSocketData>, message: Uint8Array): void {
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

    #handleSubscribe(ws: Bun.ServerWebSocket<RelayWebSocketData>, message: Uint8Array): void {
        const channel = relayReadChannel(message);
        if (!channel) {
            RelayServerLog.warn(
                `subscribe rejected id=${ws.data.connectionId} reason=missing-channel bytes=${message.byteLength}`,
            );
            return;
        }

        if (ws.isSubscribed(channel)) {
            RelayServerLog.debug(
                `subscribe ignored id=${ws.data.connectionId} channel=${channel} reason=already-subscribed`,
            );
            return;
        }

        ws.subscribe(channel);

        RelayServerLog.info(
            `subscribed id=${ws.data.connectionId} channel=${channel} subscriptions=[${ws.subscriptions.join(", ")}]`,
        );
    }

    #handleUnsubscribe(ws: Bun.ServerWebSocket<RelayWebSocketData>, message: Uint8Array): void {
        const channel = relayReadChannel(message);
        if (!channel) {
            RelayServerLog.warn(
                `unsubscribe rejected id=${ws.data.connectionId} reason=missing-channel bytes=${message.byteLength}`,
            );
            return;
        }

        if (!ws.isSubscribed(channel)) {
            RelayServerLog.debug(
                `unsubscribe ignored id=${ws.data.connectionId} channel=${channel} reason=not-subscribed`,
            );
            return;
        }

        ws.unsubscribe(channel);

        RelayServerLog.info(
            `unsubscribed id=${ws.data.connectionId} channel=${channel} subscriptions=[${ws.subscriptions.join(", ")}]`,
        );
    }

    #handleChannelMessage(ws: Bun.ServerWebSocket<RelayWebSocketData>, message: Uint8Array): void {
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

        const senderGuid = relayReadGuid(message, 1);
        // ws.publish skips the sender so it does not download its own messages,
        // but it delivers nothing when the sender has no subscriptions at all.
        const status = ws.isSubscribed(channel)
            ? ws.publish(channel, message)
            : this.#server!.publish(channel, message);

        RelayServerLog.debug(
            `published id=${ws.data.connectionId} guid=${senderGuid} channel=${channel} bytes=${message.byteLength} status=${status}`,
        );
    }
}
