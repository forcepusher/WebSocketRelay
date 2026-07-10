import {
    RelayMessageType,
    relayMessageTypeName,
    RelayMessageTopicMessageTopicLengthOffset,
    relayReadGuid,
    relayReadTopic,
    relayReadTopicLength,
} from "./RelayMessageType";
import { RelayServerLog } from "./RelayServerLog";

type RelayWebSocketData = {
    connectionId: number;
};

type RelayServerTlsOptions = {
    cert: string;
    key: string;
};

export class RelayServer {
    #port: number;
    #tls?: RelayServerTlsOptions;
    #server: Bun.Server<RelayWebSocketData> | null = null;
    #nextConnectionId = 1;

    constructor(port: number = 80, tls?: RelayServerTlsOptions) {
        this.#port = port;
        this.#tls = tls;
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
                open: (ws) => {
                    RelayServerLog.info(
                        `connected id=${ws.data.connectionId} remote=${ws.remoteAddress} subscriptions=[]`,
                    );
                },
                close: (ws) => {
                    RelayServerLog.info(
                        `disconnected id=${ws.data.connectionId} remote=${ws.remoteAddress} subscriptions=[${ws.subscriptions.join(", ")}]`,
                    );
                },
                message: (ws, message) => {
                    if (!(message instanceof Uint8Array)) {
                        RelayServerLog.warn(
                            `ignored non-binary frame id=${ws.data.connectionId} type=${typeof message}`,
                        );
                        return;
                    }

                    const type = message[0];

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
                        case RelayMessageType.TopicMessage:
                            this.#handleTopicMessage(ws, message);
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

        const scheme = this.#tls ? "wss" : "ws";
        RelayServerLog.info(
            `listening on ${scheme}://0.0.0.0:${this.#port} debug=${process.env.RELAY_DEBUG === "1" ? "verbose" : "basic"}`,
        );
    }

    stop(): void {
        if (this.#server) {
            this.#server.stop();
            this.#server = null;
            RelayServerLog.info("stopped");
        }
    }

    #handleSubscribe(ws: Bun.ServerWebSocket<RelayWebSocketData>, message: Uint8Array): void {
        const topic = relayReadTopic(message);
        if (!topic) {
            RelayServerLog.warn(
                `subscribe rejected id=${ws.data.connectionId} reason=missing-topic bytes=${message.byteLength}`,
            );
            return;
        }

        if (ws.isSubscribed(topic)) {
            RelayServerLog.debug(
                `subscribe ignored id=${ws.data.connectionId} topic=${topic} reason=already-subscribed`,
            );
            return;
        }

        ws.subscribe(topic);

        RelayServerLog.info(
            `subscribed id=${ws.data.connectionId} topic=${topic} subscriptions=[${ws.subscriptions.join(", ")}]`,
        );
    }

    #handleUnsubscribe(ws: Bun.ServerWebSocket<RelayWebSocketData>, message: Uint8Array): void {
        const topic = relayReadTopic(message);
        if (!topic) {
            RelayServerLog.warn(
                `unsubscribe rejected id=${ws.data.connectionId} reason=missing-topic bytes=${message.byteLength}`,
            );
            return;
        }

        if (!ws.isSubscribed(topic)) {
            RelayServerLog.debug(
                `unsubscribe ignored id=${ws.data.connectionId} topic=${topic} reason=not-subscribed`,
            );
            return;
        }

        ws.unsubscribe(topic);

        RelayServerLog.info(
            `unsubscribed id=${ws.data.connectionId} topic=${topic} subscriptions=[${ws.subscriptions.join(", ")}]`,
        );
    }

    #handleTopicMessage(ws: Bun.ServerWebSocket<RelayWebSocketData>, message: Uint8Array): void {
        const topicLength = relayReadTopicLength(message, RelayMessageTopicMessageTopicLengthOffset);
        if (topicLength < 0) {
            RelayServerLog.warn(
                `topic message rejected id=${ws.data.connectionId} reason=short-frame bytes=${message.byteLength}`,
            );
            return;
        }

        const topic = relayReadTopic(message, RelayMessageTopicMessageTopicLengthOffset);
        if (!topic) {
            RelayServerLog.warn(
                `topic message rejected id=${ws.data.connectionId} reason=missing-topic bytes=${message.byteLength}`,
            );
            return;
        }

        const senderGuid = relayReadGuid(message, 1);
        const deliveredTo = this.#server!.publish(topic, message);

        RelayServerLog.debug(
            `published id=${ws.data.connectionId} guid=${senderGuid} topic=${topic} bytes=${message.byteLength} deliveredTo=${deliveredTo}`,
        );
    }
}
