export class RelayServer {
    #port: number;
    #sockets: Set<Bun.ServerWebSocket>;
    #server: Bun.Server<any> | null = null;

    constructor(port: number = 23144) {
        this.#port = port;
        this.#sockets = new Set();
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
                    this.#sockets.add(ws);
                    console.log(
                        `Client connected. Total clients: ${this.#sockets.size}`,
                    );
                },
                close: (ws) => {
                    this.#sockets.delete(ws);
                    console.log(
                        `Client disconnected. Total clients: ${this.#sockets.size}`,
                    );
                },
                message: (ws, message) => {
                    for (const client of this.#sockets) {
                        if (client !== ws) {
                            client.send(message);
                        }
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
            this.#sockets.clear();
            console.log("Relay Server stopped.");
        }
    }
}
