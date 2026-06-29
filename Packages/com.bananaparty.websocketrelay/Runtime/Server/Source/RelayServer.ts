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
                        case 0x01: // JOIN_ROOM
                            this.#handleJoinRoom(ws, view);
                            break;
                        case 0x02: // LEAVE_ROOM
                            this.#handleLeaveRoom(ws, view);
                            break;
                        case 0x03: // SEND_MESSAGE
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
        if (view.byteLength < 5) return;
        const roomId = view.getInt32(1, true);

        this.#joinRoom(ws, roomId);

        // Send JOINED_ROOM confirmation
        const response = new Uint8Array(5);
        const respView = new DataView(response.buffer);
        respView.setUint8(0, 0x10);
        respView.setInt32(1, roomId, true);
        ws.send(response);
    }

    #handleLeaveRoom(ws: Bun.ServerWebSocket, view: DataView): void {
        if (view.byteLength < 5) return;
        const roomId = view.getInt32(1, true);

        this.#leaveRoom(ws, roomId);

        // Send LEFT_ROOM confirmation
        const response = new Uint8Array(5);
        const respView = new DataView(response.buffer);
        respView.setUint8(0, 0x11);
        respView.setInt32(1, roomId, true);
        ws.send(response);
    }

    #handleSendMessage(
        ws: Bun.ServerWebSocket,
        message: Uint8Array,
        view: DataView,
    ): void {
        if (view.byteLength < 5) return;
        const roomId = view.getInt32(1, true);

        const roomMembers = this.#roomSockets.get(roomId);
        if (!roomMembers) return;

        for (const client of roomMembers) {
            if (client !== ws) {
                // Build ROOM_MESSAGE: type byte + room ID + original payload (from offset 5)
                const dataLength = message.byteLength - 5;
                const response = new Uint8Array(5 + dataLength);
                const respView = new DataView(response.buffer);
                respView.setUint8(0, 0x12);
                respView.setInt32(1, roomId, true);
                response.set(message.subarray(5), 5);
                client.send(response);
            }
        }
    }

    #joinRoom(ws: Bun.ServerWebSocket, roomId: number): void {
        const socketRooms = this.#socketRooms.get(ws);
        if (!socketRooms) return;

        // Only join if not already in the room
        if (socketRooms.has(roomId)) return;

        socketRooms.add(roomId);

        if (!this.#roomSockets.has(roomId)) {
            this.#roomSockets.set(roomId, new Set());
        }
        this.#roomSockets.get(roomId)!.add(ws);
    }

    #leaveRoom(ws: Bun.ServerWebSocket, roomId: number): void {
        const socketRooms = this.#socketRooms.get(ws);
        if (socketRooms) {
            socketRooms.delete(roomId);
        }

        const roomSockets = this.#roomSockets.get(roomId);
        if (roomSockets) {
            roomSockets.delete(ws);
            if (roomSockets.size === 0) {
                this.#roomSockets.delete(roomId);
            }
        }
    }
}
