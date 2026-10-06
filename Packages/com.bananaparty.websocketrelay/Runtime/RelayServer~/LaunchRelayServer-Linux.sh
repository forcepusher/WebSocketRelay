#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SSL_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SSL_CERT="$SSL_DIR/ssl.crt"
SSL_KEY="$SSL_DIR/ssl.key"

if [[ -f "$SSL_CERT" && -f "$SSL_KEY" ]]; then
    export RELAY_PORT=443
    export RELAY_TLS_CERT="$SSL_CERT"
    export RELAY_TLS_KEY="$SSL_KEY"
    echo "SSL certificates found. Starting relay server on port 443 with WSS."
else
    export RELAY_PORT=80
    unset RELAY_TLS_CERT RELAY_TLS_KEY
    echo "No SSL certificates found. Starting relay server on port 80 with WS."
fi

BUN_PATH="$SCRIPT_DIR/Bun/bun-linux-x64/bun"
export BUN_ENABLE_CRASH_REPORTING=0

# Started again whenever it exits, because clients reconnect on their own and a crash should only cost them that.
trap "exit 0" INT TERM
while true; do
    "$BUN_PATH" --cwd "$SCRIPT_DIR" Source/index.ts -relay-server
    echo "Relay server exited with code $?. Starting it again in 1 second, press Ctrl+C to stop."
    sleep 1
done
