import {
    RelayMessageHeaderLength,
    RelayMessagePayloadOffset,
    RelayMessageType,
    relayMessageTypeName,
    relayRoomTopic,
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

                    const view = new DataView(
                        message.buffer,
                        message.byteOffset,
                        message.byteLength,
                    );
                    const type = view.getUint8(0);

                    RelayServerLog.debug(
                        `message id=${ws.data.connectionId} type=${relayMessageTypeName(type)} bytes=${message.byteLength}`,
                    );

                    switch (type) {
                        case RelayMessageType.JoinRoom:
                            this.#handleJoinRoom(ws, view);
                            break;
                        case RelayMessageType.LeaveRoom:
                            this.#handleLeaveRoom(ws, view);
                            break;
                        case RelayMessageType.SendMessage:
                            this.#handleSendMessage(ws, message, view);
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

    #handleJoinRoom(ws: Bun.ServerWebSocket<RelayWebSocketData>, view: DataView): void {
        if (view.byteLength < RelayMessageHeaderLength) {
            RelayServerLog.warn(
                `join rejected id=${ws.data.connectionId} reason=short-frame bytes=${view.byteLength}`,
            );
            return;
        }

        const roomId = view.getInt32(1, true);
        const topic = relayRoomTopic(roomId);

        if (!this.#joinRoom(ws, roomId)) {
            RelayServerLog.debug(
                `join ignored id=${ws.data.connectionId} room=${roomId} reason=already-subscribed`,
            );
            return;
        }

        const response = new Uint8Array(RelayMessageHeaderLength);
        const respView = new DataView(response.buffer);
        respView.setUint8(0, RelayMessageType.JoinedRoom);
        respView.setInt32(1, roomId, true);
        ws.send(response);

        RelayServerLog.info(
            `joined id=${ws.data.connectionId} room=${roomId} topic=${topic} subscriptions=[${ws.subscriptions.join(", ")}]`,
        );
    }

    #handleLeaveRoom(ws: Bun.ServerWebSocket<RelayWebSocketData>, view: DataView): void {
        if (view.byteLength < RelayMessageHeaderLength) {
            RelayServerLog.warn(
                `leave rejected id=${ws.data.connectionId} reason=short-frame bytes=${view.byteLength}`,
            );
            return;
        }

        const roomId = view.getInt32(1, true);
        const topic = relayRoomTopic(roomId);

        if (!this.#leaveRoom(ws, roomId)) {
            RelayServerLog.debug(
                `leave ignored id=${ws.data.connectionId} room=${roomId} reason=not-subscribed`,
            );
            return;
        }

        const response = new Uint8Array(RelayMessageHeaderLength);
        const respView = new DataView(response.buffer);
        respView.setUint8(0, RelayMessageType.LeftRoom);
        respView.setInt32(1, roomId, true);
        ws.send(response);

        RelayServerLog.info(
            `left id=${ws.data.connectionId} room=${roomId} topic=${topic} subscriptions=[${ws.subscriptions.join(", ")}]`,
        );
    }

    #handleSendMessage(
        ws: Bun.ServerWebSocket<RelayWebSocketData>,
        message: Uint8Array,
        view: DataView,
    ): void {
        if (view.byteLength < RelayMessageHeaderLength) {
            RelayServerLog.warn(
                `send rejected id=${ws.data.connectionId} reason=short-frame bytes=${view.byteLength}`,
            );
            return;
        }

        const roomId = view.getInt32(1, true);
        const topic = relayRoomTopic(roomId);

        if (!ws.isSubscribed(topic)) {
            RelayServerLog.warn(
                `send rejected id=${ws.data.connectionId} room=${roomId} reason=not-subscribed`,
            );
            return;
        }

        const payloadBytes = message.byteLength - RelayMessagePayloadOffset;
        const response = new Uint8Array(RelayMessageHeaderLength + payloadBytes);
        const respView = new DataView(response.buffer);
        respView.setUint8(0, RelayMessageType.RoomMessage);
        respView.setInt32(1, roomId, true);
        response.set(message.subarray(RelayMessagePayloadOffset), RelayMessagePayloadOffset);

        const deliveredTo = ws.publish(topic, response);

        RelayServerLog.debug(
            `published id=${ws.data.connectionId} room=${roomId} topic=${topic} payloadBytes=${payloadBytes} deliveredTo=${deliveredTo}`,
        );
    }

    #joinRoom(ws: Bun.ServerWebSocket<RelayWebSocketData>, roomId: number): boolean {
        const topic = relayRoomTopic(roomId);
        if (ws.isSubscribed(topic)) return false;

        ws.subscribe(topic);
        return true;
    }

    #leaveRoom(ws: Bun.ServerWebSocket<RelayWebSocketData>, roomId: number): boolean {
        const topic = relayRoomTopic(roomId);
        if (!ws.isSubscribed(topic)) return false;

        ws.unsubscribe(topic);
        return true;
    }
}
