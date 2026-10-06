# Relay Server

WebSocket relay server powered by [Bun](https://bun.sh/).

## Running

- **Windows:** `LaunchRelayServer-Windows.bat`
- **Linux:** `LaunchRelayServer-Linux.sh`
- **macOS:** `LaunchRelayServer-MacOS.sh`

Default port **80** (`ws://localhost`) when no TLS certificates are present. Place `ssl.crt` and `ssl.key` one folder above the server directory to enable **WSS** on port **443** (`wss://localhost`).

Export the server via **Tools → WebSocket Relay → Export Server** before running these scripts.

## Configuration

Optional environment variables:

| Variable | Default | Description |
| --- | --- | --- |
| `RELAY_PORT` | `80` | Port to listen on. |
| `RELAY_TLS_CERT`, `RELAY_TLS_KEY` | | Certificate and key paths that enable WSS. |
| `RELAY_IDLE_TIMEOUT` | `32` | Seconds without any traffic before a connection is dropped. Clients send a heartbeat every second, so only dead connections reach it. |
| `RELAY_BACKPRESSURE_LIMIT` | `1048576` | Bytes queued for a client before it is disconnected. A client that cannot keep up reconnects and resynchronizes instead of receiving stale messages. |
| `RELAY_DEBUG` | | Set to `1` for verbose logging. |
