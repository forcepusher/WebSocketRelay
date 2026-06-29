import {
    RelayMessageHeaderLength,
    RelayMessagePayloadOffset,
    RelayMessageType,
} from "./RelayMessageType";

export class RelayServer {
    #port: number;
    #server: Bun.Server<any> | null = null;

    // Maps each WebSocket to the set of room IDs it belongs to
    #socketRooms: Map<Bun.ServerWebSocket, Set<number>> = new Map();
    // Maps each room ID to the set of WebSockets in that room
    #roomSockets: Map<number, Set<Bun.ServerWebSocket>> = new Map();

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
                open: (ws) => {
                    this.#socketRooms.set(ws, new Set());
                    console.log(`Client connected.`);
                },
                close: (ws) => {
                    const rooms = this.#socketRooms.get(ws);
                    if (rooms) {
                        for (const roomId of rooms) {
                            this.#leaveRoom(ws, roomId);
                        }
                    }
                    this.#socketRooms.delete(ws);
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
            this.#socketRooms.clear();
            this.#roomSockets.clear();
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

        const roomMembers = this.#roomSockets.get(roomId);
        if (!roomMembers?.has(ws)) return;

        for (const client of roomMembers) {
            if (client !== ws) {
                const dataLength = message.byteLength - RelayMessagePayloadOffset;
                const response = new Uint8Array(RelayMessageHeaderLength + dataLength);
                const respView = new DataView(response.buffer);
                respView.setUint8(0, RelayMessageType.RoomMessage);
                respView.setInt32(1, roomId, true);
                response.set(message.subarray(RelayMessagePayloadOffset), RelayMessagePayloadOffset);
                client.send(response);
            }
        }
    }

    #joinRoom(ws: Bun.ServerWebSocket, roomId: number): boolean {
        const socketRooms = this.#socketRooms.get(ws);
        if (!socketRooms) return false;

        if (socketRooms.has(roomId)) return false;

        socketRooms.add(roomId);

        if (!this.#roomSockets.has(roomId)) {
            this.#roomSockets.set(roomId, new Set());
        }
        this.#roomSockets.get(roomId)!.add(ws);
        return true;
    }

    #leaveRoom(ws: Bun.ServerWebSocket, roomId: number): boolean {
        const socketRooms = this.#socketRooms.get(ws);
        if (!socketRooms?.has(roomId)) return false;

        socketRooms.delete(roomId);

        const roomSockets = this.#roomSockets.get(roomId);
        if (roomSockets) {
            roomSockets.delete(ws);
            if (roomSockets.size === 0) {
                this.#roomSockets.delete(roomId);
            }
        }

        return true;
    }
}
