#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUN_PATH="$SCRIPT_DIR/Bun/bun-darwin-aarch64/bun"
"$BUN_PATH" --cwd "$SCRIPT_DIR/../.." com.bananaparty.websocketrelay/Server/Source/index.ts
