# Relay Server

WebSocket relay server powered by [Bun](https://bun.sh/).

## Running

- **Windows:** `LaunchRelayServer-Windows.bat`
- **Linux:** `LaunchRelayServer-Linux.sh`
- **macOS:** `LaunchRelayServer-MacOS.sh`

Default port **80** (`ws://localhost`) when no TLS certificates are present. Place `ssl.crt` and `ssl.key` one folder above the server directory to enable **WSS** on port **443** (`wss://localhost`).

Export the server via **Tools → WebSocket Relay → Export Server** before running these scripts.

The scripts start the server again whenever it exits, one second later, until you stop them with Ctrl+C. Clients reconnect on their own, so a crash only costs them a reconnect.

Bun's crash reporter is turned off, because on Windows it kept the port open after a crash so no new server could start.
Bun 1.3 on Windows crashes when an anti-cheat such as nProtect GameGuard (Helldivers 2 and others) or some security software injects into processes, see [oven-sh/bun#34055](https://github.com/oven-sh/bun/issues/34055). The bundled Bun 1.4.2 includes the fix, [oven-sh/bun#35083](https://github.com/oven-sh/bun/pull/35083).

## Configuration

Optional environment variables:

| Variable | Default | Description |
| --- | --- | --- |
| `RELAY_PORT` | `80` | Port to listen on. |
| `RELAY_TLS_CERT`, `RELAY_TLS_KEY` | | Certificate and key paths that enable WSS. |
| `RELAY_DEBUG` | | Set to `1` for verbose logging. |

A connection with no traffic for 60 seconds is closed. Clients heartbeat every second, so this only removes a dead socket. A client with more than 4 MiB queued is disconnected and resynchronizes after reconnecting.
