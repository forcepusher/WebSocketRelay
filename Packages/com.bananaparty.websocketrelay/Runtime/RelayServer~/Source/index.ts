import { RelayServer } from "./RelayServer";

const port = Number(process.env.RELAY_PORT) || 80;
const certPath = process.env.RELAY_TLS_CERT;
const keyPath = process.env.RELAY_TLS_KEY;
const tls = certPath && keyPath ? { cert: certPath, key: keyPath } : undefined;

const server = new RelayServer(port, tls, {
    idleTimeoutSeconds: Number(process.env.RELAY_IDLE_TIMEOUT) || undefined,
    backpressureLimitBytes: Number(process.env.RELAY_BACKPRESSURE_LIMIT) || undefined,
});
server.start();
