import {
    RelayMessageHeaderLength,
    RelayMessagePayloadOffset,
    RelayMessageType,
    relayRoomTopic,
} from "./RelayMessageType";

export class RelayServer {
    #port: number;
    #server: Bun.Server<any> | null = null;

    constructor(port: number = 23144) {
        this.#port = port;
    }

    start(): void {
        this.#server = Bun.serve({
            port: this.#port,
            fetch: (req, server) => {
                if (server.upgrade(req)) {
                    return undefined;
                }
                return new Response("WebSocket Relay Server");
            },
            websocket: {
                open: () => {
                    console.log(`Client connected.`);
                },
                close: () => {
                    console.log(`Client disconnected.`);
                },
                message: (ws, message) => {
                    if (!(message instanceof Uint8Array)) return;

                    const view = new DataView(
                        message.buffer,
                        message.byteOffset,
                        message.byteLength,
                    );
                    const type = view.getUint8(0);

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
                    }
                },
            },
        });

        console.log(`Relay Server listening on port ${this.#port}`);
    }

    stop(): void {
        if (this.#server) {
            this.#server.stop();
            this.#server = null;
            console.log("Relay Server stopped.");
        }
    }

    #handleJoinRoom(ws: Bun.ServerWebSocket, view: DataView): void {
        if (view.byteLength < RelayMessageHeaderLength) return;
        const roomId = view.getInt32(1, true);

        if (!this.#joinRoom(ws, roomId)) return;

        const response = new Uint8Array(RelayMessageHeaderLength);
        const respView = new DataView(response.buffer);
        respView.setUint8(0, RelayMessageType.JoinedRoom);
        respView.setInt32(1, roomId, true);
        ws.send(response);
    }

    #handleLeaveRoom(ws: Bun.ServerWebSocket, view: DataView): void {
        if (view.byteLength < RelayMessageHeaderLength) return;
        const roomId = view.getInt32(1, true);

        if (!this.#leaveRoom(ws, roomId)) return;

        const response = new Uint8Array(RelayMessageHeaderLength);
        const respView = new DataView(response.buffer);
        respView.setUint8(0, RelayMessageType.LeftRoom);
        respView.setInt32(1, roomId, true);
        ws.send(response);
    }

    #handleSendMessage(
        ws: Bun.ServerWebSocket,
        message: Uint8Array,
        view: DataView,
    ): void {
        if (view.byteLength < RelayMessageHeaderLength) return;
        const roomId = view.getInt32(1, true);
        const topic = relayRoomTopic(roomId);

        if (!ws.isSubscribed(topic)) return;

        const dataLength = message.byteLength - RelayMessagePayloadOffset;
        const response = new Uint8Array(RelayMessageHeaderLength + dataLength);
        const respView = new DataView(response.buffer);
        respView.setUint8(0, RelayMessageType.RoomMessage);
        respView.setInt32(1, roomId, true);
        response.set(message.subarray(RelayMessagePayloadOffset), RelayMessagePayloadOffset);

        ws.publish(topic, response);
    }

    #joinRoom(ws: Bun.ServerWebSocket, roomId: number): boolean {
        const topic = relayRoomTopic(roomId);
        if (ws.isSubscribed(topic)) return false;

        ws.subscribe(topic);
        return true;
    }

    #leaveRoom(ws: Bun.ServerWebSocket, roomId: number): boolean {
        const topic = relayRoomTopic(roomId);
        if (!ws.isSubscribed(topic)) return false;

        ws.unsubscribe(topic);
        return true;
    }
}
