#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SSL_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SSL_CERT="$SSL_DIR/ssl.crt"
SSL_KEY="$SSL_DIR/ssl.key"

if [[ -f "$SSL_CERT" && -f "$SSL_KEY" ]]; then
    export RELAY_PORT=443
    export RELAY_TLS_CERT="$SSL_CERT"
    export RELAY_TLS_KEY="$SSL_KEY"
    echo "SSL certificates found. Starting relay server on port 443 (wss://)"
else
    export RELAY_PORT=80
    unset RELAY_TLS_CERT RELAY_TLS_KEY
    echo "No SSL certificates found. Starting relay server on port 80 (ws://)"
fi

BUN_PATH="$SCRIPT_DIR/Bun/bun-linux-x64/bun"
"$BUN_PATH" --cwd "$SCRIPT_DIR/../../.." com.bananaparty.websocketrelay/Runtime/Server~/Source/index.ts -relay-server
