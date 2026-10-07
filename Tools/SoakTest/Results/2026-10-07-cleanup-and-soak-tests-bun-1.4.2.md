# Soak run: this branch, relay on Bun 1.4.2

6 clients (client 5 at 30 fps, the rest at 60 fps), JSON state, 350 s.

Relay server crashes, restarted by the harness: 0.

**All checks passed.**


| Phase | Reconnects per client | Min players seen | Max state latency (ms) | Max RPC latency (ms) | RPC gaps | Max pending send (KB) | Owners agree | Relay crashes | Result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| warmup | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 34 | 51 | 0 | 1 | yes | 0 | pass |
| latency-jitter | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 282 | 285 | 0 | 1 | yes | 0 | pass |
| packet-loss | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 1263 | 1280 | 0 | 1 | yes | 0 | pass |
| slow-link | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 13425 | 13441 | 0 | 1 | no | 0 | pass |
| short-stall | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 15625 | 15642 | 0 | 1 | yes | 0 | pass |
| long-stall | 0 / 1 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 33 | 3050 | 330 | 1 | yes | 0 | pass |
| blackout | 0 / 0 / 1 / 0 / 0 / 0 | 4 / 4 / 5 / 4 / 4 / 4 | 34 | 11045 | 530 | 1 | yes | 0 | pass |
| reset-all | 1 / 1 / 1 / 1 / 1 / 1 | 5 / 5 / 5 / 5 / 5 / 5 | 35 | 230 | 43 | 1 | yes | 0 | pass |
| relay-restart | 1 / 1 / 1 / 1 / 1 / 1 | 5 / 5 / 5 / 5 / 5 / 5 | 34 | 6808 | 478 | 1 | yes | 0 | pass |
| short-hitch | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 2993 | 2896 | 0 | 1 | yes | 0 | pass |
| long-hitch | 1 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 6984 | 6872 | 0 | 1 | yes | 0 | pass |
| rpc-burst | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 49 | 68 | 0 | 1 | yes | 0 | pass |
| high-rate | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 35 | 51 | 0 | 1 | yes | 0 | pass |
| cooldown | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 35 | 50 | 0 | 1 | yes | 0 | pass |

| Client | RPCs sent | RPCs received | Gaps | Duplicates | Max state latency (ms) | Max RPC latency (ms) | Reconnects | Gave up | Exceptions | Errors |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 2561 | 12684 | 115 | 0 | 6984 | 11037 | 3 | 0 | 0 | 0 |
| 1 | 2561 | 12536 | 263 | 0 | 3000 | 11037 | 3 | 0 | 0 | 0 |
| 2 | 2560 | 12365 | 434 | 0 | 2995 | 6806 | 3 | 0 | 0 | 0 |
| 3 | 2561 | 12570 | 229 | 0 | 15625 | 15642 | 2 | 0 | 0 | 0 |
| 4 | 2560 | 12651 | 148 | 0 | 3059 | 11037 | 2 | 0 | 0 | 0 |
| 5 | 2561 | 12606 | 192 | 0 | 2998 | 11034 | 2 | 0 | 0 | 0 |
