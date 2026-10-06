# Soak run: after (this branch)

6 clients (client 5 at 30 fps, the rest at 60 fps), JSON state, 351 s.

Relay server crashes, restarted by the harness: 0.

**All checks passed.**


| Phase | Reconnects per client | Min players seen | Max state latency (ms) | Max RPC latency (ms) | RPC gaps | Max pending send (KB) | Owners agree | Relay crashes | Result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| warmup | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 35 | 52 | 0 | 1 | yes | 0 | pass |
| latency-jitter | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 282 | 301 | 0 | 1 | yes | 0 | pass |
| packet-loss | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 1285 | 1318 | 0 | 1 | yes | 0 | pass |
| slow-link | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 15404 | 15271 | 0 | 1 | no | 0 | pass |
| short-stall | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 17604 | 17471 | 0 | 1 | yes | 0 | pass |
| long-stall | 0 / 1 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 34 | 2965 | 325 | 1 | yes | 0 | pass |
| blackout | 0 / 0 / 1 / 0 / 0 / 0 | 4 / 4 / 5 / 4 / 4 / 4 | 34 | 10964 | 525 | 1 | yes | 0 | pass |
| reset-all | 1 / 1 / 1 / 1 / 1 / 1 | 5 / 5 / 5 / 5 / 5 / 5 | 34 | 200 | 15 | 1 | yes | 0 | pass |
| relay-restart | 1 / 1 / 1 / 1 / 1 / 1 | 5 / 5 / 5 / 5 / 5 / 5 | 34 | 7002 | 468 | 1 | yes | 0 | pass |
| short-hitch | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 3007 | 2874 | 0 | 1 | yes | 0 | pass |
| long-hitch | 1 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 7002 | 6874 | 5 | 1 | yes | 0 | pass |
| rpc-burst | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 49 | 70 | 0 | 1 | yes | 0 | pass |
| high-rate | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 34 | 52 | 0 | 1 | yes | 0 | pass |
| cooldown | 0 / 0 / 0 / 0 / 0 / 0 | 5 / 5 / 5 / 5 / 5 / 5 | 33 | 52 | 0 | 1 | yes | 0 | pass |

| Client | RPCs sent | RPCs received | Gaps | Duplicates | Max state latency (ms) | Max RPC latency (ms) | Reconnects | Gave up | Exceptions | Errors |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 2559 | 12707 | 84 | 0 | 7002 | 10948 | 3 | 0 | 0 | 0 |
| 1 | 2558 | 12442 | 347 | 0 | 2952 | 10951 | 3 | 0 | 0 | 0 |
| 2 | 2558 | 12185 | 605 | 0 | 2951 | 2950 | 3 | 0 | 0 | 0 |
| 3 | 2559 | 12653 | 137 | 0 | 17604 | 17471 | 2 | 0 | 0 | 0 |
| 4 | 2558 | 12735 | 53 | 0 | 3080 | 10948 | 2 | 0 | 0 | 0 |
| 5 | 2559 | 12678 | 112 | 0 | 2966 | 10964 | 2 | 0 | 0 | 0 |
