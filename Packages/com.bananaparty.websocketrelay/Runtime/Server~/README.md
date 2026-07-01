# Relay Server

WebSocket relay server powered by [Bun](https://bun.sh/).

## Running

- **Windows:** `LaunchServer-Windows.bat`
- **Linux:** `LaunchServer-Linux.sh`
- **macOS:** `LaunchServer-MacOS.sh`

Default port **23144** (`ws://localhost:23144`) — leet **RELAY** (`R3L4Y`).

## Why `--cwd` in the launch scripts?

The Unity UPM manifest (`../package.json`) contains `"type": "library"` which is valid for Unity but not for npm/Bun. Bun performs two directory walks on startup:

1. **Project root discovery** — walks up from the process **CWD** looking for workspace roots, lockfiles, etc. It reads and validates every `package.json` it encounters going upward.

2. **Module type resolution** — walks up from the **source file's location** to find the nearest `package.json` and stops at the first one it finds.

Without `--cwd`, both walks start from `Server/`. Walk #1 goes up and hits `../package.json` (`"type": "library"`) → Bun warns.

The fix: launch scripts set `--cwd` to **two levels up** (the parent of `com.bananaparty.websocketrelay/`). This way:

- Walk #1 starts above the UPM folder and goes upward — never encounters the Unity manifest.
- Walk #2 starts from `Source/index.ts`, goes up, finds `Server/package.json` (`"type": "module"`) — stops there, never reaches the Unity manifest.

### Walk #1 — Project root discovery (from CWD, upward)

```
E:\                                             4. no package.json, stop (filesystem root)
  Repos\                                        3. no package.json, keep going
    WebSocketRelay\                             2. no package.json, keep going
      Packages\                                 ← CWD starts here, walks UP
        com.bananaparty.websocketrelay\
          package.json                          ← BELOW cwd, never visited (child, not parent)
          Server\
```

Bun only walks toward the filesystem root. `com.bananaparty.websocketrelay/` is a child directory — invisible to this walk.

### Walk #2 — Module type resolution (from source file, upward, stops at first match)

```
E:\Repos\WebSocketRelay\Packages\
  com.bananaparty.websocketrelay\
    package.json                                ← NEVER REACHED (walk stopped below)
    Server\
      package.json                              2. FOUND "type": "module" → STOP
      Source\
        index.ts                                1. no package.json, keep going UP
```

This walk finds `Server/package.json` first and stops. It never continues up to the Unity manifest.

### Without the fix (CWD = Server/, no --cwd override)

```
E:\                                             5. no package.json, stop
  Repos\                                        4. no package.json, keep going
    WebSocketRelay\                             3. no package.json, keep going
      Packages\                                 2. no package.json, keep going
        com.bananaparty.websocketrelay\
          package.json                          ⚠️ FOUND "type": "library" → WARN!
          Server\                               ← CWD starts here, walks UP
```

Here the walk passes through `com.bananaparty.websocketrelay/` because it's a **parent** of CWD.
