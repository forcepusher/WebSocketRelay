# Soak results

Measured on 2026-10-07 on a Windows 10 desktop, with Unity 6000.3.10f1 Mono standalone builds, everything on localhost.
The relay ran on Bun 1.3.14, except in the last run.
Each run is the full set of phases described in [the soak README](../README.md).

| Run | Code | Result |
| --- | --- | --- |
| [Baseline](2026-10-07-baseline-035ed8e.md) | `035ed8e`, with the reconnect and heartbeat work | 4 checks failed. Messages arrived up to 108 s late on a clean link, the 8 s and 16 s outages went unnoticed, and clients never agreed on owners. |
| [This branch](2026-10-07-cleanup-and-soak-tests.md) | `feature/cleanup-and-soak-tests` | All checks passed. State arrived within 50 ms on a clean link, every outage was detected and repaired, and owners converged. |
| [With a WebGL client](2026-10-07-cleanup-and-soak-tests-webgl.md) | Same, plus a seventh client in a WebGL build running in Chromium | All checks passed. The browser tab was hidden and throttled to about 1 frame per second, so the WebGL client saw state about 1 s late, but it stayed in the session through every phase. |
| [Relay on Bun 1.4.2](2026-10-07-cleanup-and-soak-tests-bun-1.4.2.md) | Same as this branch, with the relay on Bun 1.4.2 | All checks passed, with delays and losses close to the run on Bun 1.3.14. |

nProtect GameGuard, the anti-cheat of Helldivers 2, was running on the machine. It crashes every Bun 1.3 process it injects into
([oven-sh/bun#34055](https://github.com/oven-sh/bun/issues/34055), with a fix in Bun 1.4), so other runs had to restart the relay many times.
The runs above had no relay crash, which their summaries confirm. The game was closed during the Bun 1.4.2 run, so that run says nothing about GameGuard.
