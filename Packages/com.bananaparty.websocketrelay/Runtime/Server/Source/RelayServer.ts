import {
    RelayMessageType,
    relayMessageTypeName,
    relayPayloadOffset,
    relayReadTopic,
    relayReadTopicLength,
    relayWriteMessage,
} from "./RelayMessageType";
import { RelayServerLog } from "./RelayServerLog";

type RelayWebSocketData = {
    connectionId: number;
};

export class RelayServer {
    #port: number;
    #server: Bun.Server<RelayWebSocketData> | null = null;
    #nextConnectionId = 1;

    constructor(port: number = 23144) {
        this.#port = port;
    }

    start(): void {
        this.#server = Bun.serve<RelayWebSocketData>({
            port: this.#port,
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
                        case RelayMessageType.Send:
                            this.#handleSend(ws, message);
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

        RelayServerLog.info(
            `listening on port ${this.#port} debug=${process.env.RELAY_DEBUG === "1" ? "verbose" : "basic"}`,
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

        if (!this.#subscribe(ws, topic)) {
            RelayServerLog.debug(
                `subscribe ignored id=${ws.data.connectionId} topic=${topic} reason=already-subscribed`,
            );
            return;
        }

        ws.send(relayWriteMessage(RelayMessageType.Subscribed, topic));

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

        if (!this.#unsubscribe(ws, topic)) {
            RelayServerLog.debug(
                `unsubscribe ignored id=${ws.data.connectionId} topic=${topic} reason=not-subscribed`,
            );
            return;
        }

        ws.send(relayWriteMessage(RelayMessageType.Unsubscribed, topic));

        RelayServerLog.info(
            `unsubscribed id=${ws.data.connectionId} topic=${topic} subscriptions=[${ws.subscriptions.join(", ")}]`,
        );
    }

    #handleSend(ws: Bun.ServerWebSocket<RelayWebSocketData>, message: Uint8Array): void {
        const topicLength = relayReadTopicLength(message);
        if (topicLength < 0) {
            RelayServerLog.warn(
                `send rejected id=${ws.data.connectionId} reason=short-frame bytes=${message.byteLength}`,
            );
            return;
        }

        const topic = relayReadTopic(message);
        if (!topic) {
            RelayServerLog.warn(
                `send rejected id=${ws.data.connectionId} reason=missing-topic bytes=${message.byteLength}`,
            );
            return;
        }

        if (!ws.isSubscribed(topic)) {
            RelayServerLog.warn(
                `send rejected id=${ws.data.connectionId} topic=${topic} reason=not-subscribed`,
            );
            return;
        }

        const payloadOffset = relayPayloadOffset(topicLength);
        const payload = message.subarray(payloadOffset);
        const response = relayWriteMessage(RelayMessageType.TopicMessage, topic, payload);

        const deliveredTo = ws.publish(topic, response);

        RelayServerLog.debug(
            `published id=${ws.data.connectionId} topic=${topic} payloadBytes=${payload.byteLength} deliveredTo=${deliveredTo}`,
        );
    }

    #subscribe(ws: Bun.ServerWebSocket<RelayWebSocketData>, topic: string): boolean {
        if (ws.isSubscribed(topic)) return false;

        ws.subscribe(topic);
        return true;
    }

    #unsubscribe(ws: Bun.ServerWebSocket<RelayWebSocketData>, topic: string): boolean {
        if (!ws.isSubscribed(topic)) return false;

        ws.unsubscribe(topic);
        return true;
    }
}
