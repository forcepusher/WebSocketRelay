# Relay Server

WebSocket relay server powered by [Bun](https://bun.sh/).

## Running

- **Windows:** `LaunchServer-Windows.bat`
- **Linux:** `LaunchServer-Linux.sh`
- **macOS:** `LaunchServer-MacOS.sh`

Default port **80** (`ws://localhost`) when no TLS certificates are present. Place `ssl.crt` and `ssl.key` one folder above the server directory to enable **WSS** on port **443** (`wss://localhost`).

Export the server via **Tools → WebSocket Relay → Export Server** before running these scripts.
