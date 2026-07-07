import { RelayServer } from "./RelayServer";

const port = Number(process.env.RELAY_PORT) || 23144;
const server = new RelayServer(port);
server.start();
