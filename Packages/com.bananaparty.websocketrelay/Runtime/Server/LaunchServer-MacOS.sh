#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

stop_relay_server() {
  pkill -f "Source/index.ts -relay-server" 2>/dev/null || true
}

if [ "$1" = "stop" ]; then
  stop_relay_server
  exit 0
fi

stop_relay_server

if [ -f "$SCRIPT_DIR/../../package.json" ]; then
  RELAY_CWD="$(cd "$SCRIPT_DIR/../../.." && pwd)"
  RELAY_ENTRY="com.bananaparty.websocketrelay/Runtime/Server/Source/index.ts"
else
  RELAY_CWD="$SCRIPT_DIR"
  RELAY_ENTRY="Source/index.ts"
fi

BUN_PATH="$SCRIPT_DIR/Bun/bun-darwin-aarch64/bun"
"$BUN_PATH" --cwd "$RELAY_CWD" "$RELAY_ENTRY" -relay-server
