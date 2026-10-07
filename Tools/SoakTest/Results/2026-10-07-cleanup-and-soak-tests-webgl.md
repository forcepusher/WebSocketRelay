# Soak run: after with a WebGL client

7 clients (client 5 at 30 fps, the rest at 60 fps), JSON state, 358 s.

Relay server crashes, restarted by the harness: 0.

**All checks passed.**


| Phase | Reconnects per client | Min players seen | Max state latency (ms) | Max RPC latency (ms) | RPC gaps | Max pending send (KB) | Owners agree | Relay crashes | Result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| warmup | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 1001 | 1031 | 0 | 1 | no | 0 | pass |
| latency-jitter | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 1686 | 1702 | 0 | 1 | yes | 0 | pass |
| packet-loss | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 2046 | 2153 | 0 | 1 | yes | 0 | pass |
| slow-link | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 15481 | 15921 | 0 | 1 | no | 0 | pass |
| short-stall | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 17806 | 17921 | 0 | 1 | yes | 0 | pass |
| long-stall | 0 / 1 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 988 | 3104 | 390 | 1 | yes | 0 | pass |
| blackout | 0 / 0 / 1 / 0 / 0 / 0 / 0 | 5 / 5 / 6 / 5 / 5 / 5 / 6 | 1003 | 11088 | 630 | 1 | yes | 0 | pass |
| reset-all | 1 / 1 / 1 / 1 / 1 / 1 / 1 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 1004 | 3029 | 83 | 2 | no | 0 | pass |
| relay-restart | 1 / 1 / 1 / 1 / 1 / 1 / 1 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 1003 | 7104 | 689 | 4 | yes | 0 | pass |
| short-hitch | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 3003 | 3366 | 0 | 1 | no | 0 | pass |
| long-hitch | 1 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 7020 | 7350 | 5 | 1 | no | 0 | pass |
| rpc-burst | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 1003 | 1030 | 0 | 20 | yes | 0 | pass |
| high-rate | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 1003 | 1036 | 0 | 4 | yes | 0 | pass |
| cooldown | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 6 / 6 / 6 / 6 | 1003 | 1036 | 0 | 4 | yes | 0 | pass |

| Client | RPCs sent | RPCs received | Gaps | Duplicates | Max state latency (ms) | Max RPC latency (ms) | Reconnects | Gave up | Exceptions | Errors |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 2606 | 15438 | 147 | 0 | 7020 | 10934 | 3 | 0 | 0 | 0 |
| 1 | 2605 | 15258 | 327 | 0 | 3008 | 10934 | 3 | 0 | 0 | 0 |
| 2 | 2606 | 15047 | 539 | 0 | 3014 | 7051 | 3 | 0 | 0 | 0 |
| 3 | 2606 | 15368 | 218 | 0 | 17806 | 17921 | 2 | 0 | 0 | 0 |
| 4 | 2606 | 15407 | 179 | 0 | 3009 | 10934 | 2 | 0 | 0 | 0 |
| 5 | 2606 | 15314 | 271 | 0 | 3035 | 10950 | 2 | 0 | 0 | 0 |
| 6 | 2565 | 15201 | 116 | 0 | 3194 | 11088 | 2 | 0 | 0 | 0 |
