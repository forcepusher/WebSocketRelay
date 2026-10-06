# Soak test

Runs the real relay server, six real client processes and a fault-injecting TCP proxy in front of every client.
Then it degrades the network or the clients one phase at a time and checks how the session copes.

The clients are headless standalone builds of `Assets/SoakTest`. Each one spawns an avatar that tours four shared scene
objects with distance-based authority, so owners keep changing hands. It syncs state 10 times a second, sends 5 numbered
RPCs per second to a scene object, and reports what it sees to the harness every second. Client 5 runs at 30 fps, the rest at 60.

## Running

Build the client into a throwaway copy of the project, so the editor can stay open:

```
node build.mjs --out <build folder>
node build.mjs --out <build folder> --revision <git ref>
```

Without `--revision` the working tree is built. The client sources always come from the working tree, so the revision needs the networking API they use, which rules out versions before 3.0.0.

Then run the phases:

```
node soak.mjs --client <build folder>/Windows/SoakClient.exe --relayDir <build folder>/Project/Packages/com.bananaparty.websocketrelay/Runtime/RelayServer~ --out <results folder>
```

Add `--binary` for binary state or `--quick` for half-length phases. The run takes about 6 minutes and writes `summary.md`, `analysis.json`, `reports.json` and the logs of the relay server, every client and the harness.

## Phases

| Phase | What happens |
| --- | --- |
| warmup | Clean network. |
| latency-jitter | Client 1 gets 150 ms latency with up to 100 ms jitter each way. |
| packet-loss | Client 2 gets 80 ms latency, and 3% of chunks are held 0.3 to 1.2 s, which is how lost packets look over TCP. |
| slow-link | Client 3 is limited to 16 KB/s each way, less than the session needs. |
| short-stall | Client 4's link stalls for 3 s, shorter than the heartbeat timeout. |
| long-stall | Client 1's link stalls for 8 s, longer than the heartbeat timeout but shorter than the player timeout. |
| blackout | Client 2's link stalls for 16 s, longer than the player timeout, so peers drop it until it is back. |
| reset-all | Every connection is reset at once, like a NAT or Wi-Fi change. |
| relay-restart | The relay server is killed and started again 4 s later. |
| short-hitch | Client 3's main thread freezes for 3 s, like loading a level. |
| long-hitch | Client 0's main thread freezes for 7 s, like an app in the background. |
| rpc-burst | Every client sends 200 RPCs in one frame. |
| high-rate | Every client sends 30 RPCs per second instead of 5. |
| cooldown | Clean network, and the avatars stop moving so owners settle. |

At the end of every phase each client must be connected, see every other player and avatar exactly once, and have logged
no exceptions. Reconnects must match what the phase caused. Once the avatars stop, every client must agree on who owns
each shared object.
RPC sequence numbers show lost and duplicated messages, and timestamps show how late state and RPCs arrive.

If the relay server dies, the harness starts it again like a process supervisor would and reports the crash.
