# Changelog  
All notable changes to this project will be documented in this file.  
  
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),  
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).  
  
## [3.0.0] - 2026-10-07  
### Added  
- Automatic reconnect. A lost connection is reestablished with the same client GUID and subscriptions, while the session, owned identities and queued RPCs are kept. Backoff with jitter, gives up after `ReconnectTimeoutSeconds`.  
- Heartbeats. Clients ping the relay every second, so dead and stalled connections are noticed within `HeartbeatTimeoutSeconds` instead of hanging forever. Requires the updated relay server.  
- `RelayConnectionSettings` for connect timeout, heartbeat, reconnect and send backlog tuning. Accepted by `Network` and `RelayClient` constructors.  
- `RelayConnectionState` with `Network.ConnectionState` and `RelayClient.State`, plus `RoundTripTimeSeconds`, `IsLinkHealthy`, `PendingSendBytes` and `IsSendBacklogged`.  
- `NetworkContext.IsConnectionInterrupted`. While the local connection is silent, player timeouts and distance-based authority claims pause, so peers are not dropped and objects are not stolen because of a local outage.  
- `ISocket.IsClosed`, `ISocket.CloseReason` and `ISocket.PendingSendBytes`. Disconnect reasons are now reported and logged.  
- The relay binds every connection to the client guid from its hello message and closes connections that send with another guid, so clients cannot speak for each other. The guid of a dropped connection stays reserved for its client for 60 seconds.  
- `RelayClient.SendState` for state that the next state replaces. The relay skips it for receivers that fell behind, so a client on a slow link catches up on current state instead of working through stale state. The relay tells how far behind a receiver is by timing WebSocket pings, which wait behind everything already on its way to the receiver. Network syncs use it.  
  
### Changed  
- **Breaking:** The relay protocol has new hello and channel state messages, so clients and relay servers only work with others from 3.0.0 on.  
- **Breaking:** `IRpcTarget.ReceiveRpc` receives the guid of the sender as checked by the relay, or of the local client for RPCs invoked locally.  
- **Breaking:** Authority claims and state syncs no longer carry an owner guid. Receivers make the relay-checked sender the owner, so no client can claim or sync an identity for another.  
- **Breaking:** `IRelayListener.OnDisconnectedFromRelay` is replaced by `OnConnectionStateChanged(previousState, state, reason)`.  
- **Breaking:** `ISocket` has new members that custom implementations need to provide.  
- **Breaking:** A lost connection no longer clears the session right away. It is cleared only when reconnecting gives up. Set `ReconnectTimeoutSeconds` to 0 for the old behavior.  
- **Breaking:** `INetworkIdentity` no longer extends `INetworkState`. It declares `WriteNetworkState` and the two-argument `ReadNetworkState` itself, and adds `IsSceneBound`.  
- **Breaking:** Removed members that nothing used: `INetworkState.NetworkStateName`, `IStateInput.BeginArrayElement`, `IStateOutput.BeginArrayElement`, `NetworkContext.UseBinary` and `BinaryStateOutput.GetBuffer`. Use `NetworkContext.StateFormat` and `BinaryStateOutput.ToArray` instead.  
- **Breaking:** `Network` implements `IRelayListener` explicitly, so the relay callbacks are no longer part of its public API.  
- **Breaking:** `NetworkPlayerRoster.RemoveTimedOut` fills a list passed to it instead of allocating a new one every frame.  
- `RelayClient.Send` returns false instead of throwing while not connected. Subscriptions made while not connected are sent once connected.  
- Network syncs are skipped while the send backlog exceeds `SendBacklogLimitBytes`, so a slow connection catches up on fresh state instead of queueing stale state.  
- The relay server no longer echoes channel messages back to their sender, disconnects clients that fall more than 4 MiB behind, and drops connections idle for 60 seconds.  
- Default player timeout is 10 seconds, above the 5 second heartbeat timeout, so players have time to reconnect.  
- A single long frame advances player timeouts by at most 0.25 seconds.  
- Scene objects with a `NetworkBinding` are no longer destroyed when their owner leaves, they only lose the owner, because they cannot be spawned again.  
- `NetworkContext.ClearNetworkSession`, and so `Network.Disconnect` and giving up on reconnecting, keeps scene objects with a `NetworkBinding` and only clears their owner, for the same reason.  
- The relay launch scripts start the server again whenever it exits, so a crash only costs clients a reconnect.  
- The relay server runs with Bun's crash reporter turned off. On Windows the reporter kept the port open after a crash, so no new server could start.  
- The bundled Bun is 1.4.2 instead of 1.3.14. Bun 1.3 on Windows crashes when anti-cheat or security software injects into it, such as nProtect GameGuard ([oven-sh/bun#34055](https://github.com/oven-sh/bun/issues/34055)).  
  
### Fixed  
- Connections that died without a close frame were never detected.  
- Sending on a connection that just closed threw exceptions.  
- WebGL sockets leaked their slot and could send while still connecting or already closing.  
- A long frame, such as loading a scene, timed out every player.  
- Standalone sockets sent and received about one message per frame, so sessions with more than a few players fell further behind every second. In a soak test with 6 clients, state arrived up to 48 seconds late at 60 fps and 108 seconds late at 30 fps. Sending and receiving now run on the thread pool.  
- Messages that arrived right before a connection dropped, or during a frame long enough to trigger a reconnect, were discarded.  
- WebGL sockets kept buffering incoming messages while the application ran no frames, for example in a hidden tab. Past 4 MiB the connection is now dropped, and restored once the application runs again.  
- Strings with quotes, backslashes or control characters broke JSON state.  
- JSON state was missing the comma after an empty object or array.  
- JSON state rounded floats to 7 digits, so 123456.79 arrived as 123456.8.  
- `Network.StartServer` could not find the bundled relay server when the package was installed from git rather than placed in the Packages folder.  
- Stopping relay servers failed when a `bun` process of another user or an elevated one was running.  
- The Linux and macOS Bun runtimes and launch scripts were stored without the executable bit, so the server could not start from a fresh checkout or from a server exported on Windows.  
  
## [2.2.0] - 2026-08-06  
### Added  
- Added configurable authority interception distance for distance-based authority origins.  
  
## [2.1.2] - 2026-07-31  
### Added  
- `Network.Disconnect(bool clearSession = true)` so failed-connect offline fallback can tear down the transport without destroying scene networked objects.  
  
## [2.1.1] - 2026-07-25  
### Fixed  
- A ton of authority ownership bugs.  
  
## [2.1.0] - 2026-07-22  
### Added  
- Added "Offline mode", so your online game code can run offline without changes.  
  
## [2.0.0] - 2026-07-17  
### Added  
- Relay server and networking libraries, this is no longer just a client library.  
  
[3.0.0] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/2.2.0...3.0.0  
[2.2.0] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/2.1.2...2.2.0  
[2.1.2] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/2.1.1...2.1.2  
[2.1.1] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/2.1.0...2.1.1  
[2.1.0] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/2.0.0...2.1.0  
[2.0.0] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/1.1.1...2.0.0  
