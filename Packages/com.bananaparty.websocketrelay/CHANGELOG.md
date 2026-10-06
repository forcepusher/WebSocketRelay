# Changelog  
All notable changes to this project will be documented in this file.  
  
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),  
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).  
  
## [Unreleased]  
### Added  
- Automatic reconnect. A lost connection is reestablished with the same client GUID and subscriptions, while the session, owned identities and queued RPCs are kept. Backoff with jitter, gives up after `ReconnectTimeoutSeconds`.  
- Heartbeats. Clients ping the relay every second, so dead and stalled connections are noticed within `HeartbeatTimeoutSeconds` instead of hanging forever. Requires the updated relay server.  
- `RelayConnectionSettings` for connect timeout, heartbeat, reconnect and send backlog tuning. Accepted by `Network` and `RelayClient` constructors.  
- `RelayConnectionState` with `Network.ConnectionState` and `RelayClient.State`, plus `RoundTripTimeSeconds`, `IsLinkHealthy`, `PendingSendBytes` and `IsSendBacklogged`.  
- `NetworkContext.IsConnectionInterrupted`. While the local connection is silent, player timeouts and distance-based authority claims pause, so peers are not dropped and objects are not stolen because of a local outage.  
- `ISocket.IsClosed`, `ISocket.CloseReason` and `ISocket.PendingSendBytes`. Disconnect reasons are now reported and logged.  
  
### Changed  
- **Breaking:** `IRelayListener.OnDisconnectedFromRelay` is replaced by `OnConnectionStateChanged(previousState, state, reason)`.  
- **Breaking:** `ISocket` has new members that custom implementations need to provide.  
- **Breaking:** A lost connection no longer clears the session right away. It is cleared only when reconnecting gives up. Set `ReconnectTimeoutSeconds` to 0 for the old behavior.  
- `RelayClient.Send` returns false instead of throwing while not connected. Subscriptions made while not connected are sent once connected.  
- Network syncs are skipped while the send backlog exceeds `SendBacklogLimitBytes`, so a slow connection catches up on fresh state instead of queueing stale state.  
- The relay server no longer echoes channel messages back to their sender, disconnects clients that fall more than 4 MiB behind, and drops connections idle for 60 seconds.  
- Default player timeout is 10 seconds, above the 5 second heartbeat timeout, so players have time to reconnect.  
- A single long frame advances player timeouts by at most 0.25 seconds.  
- Scene objects with a `NetworkBinding` are no longer destroyed when their owner leaves, they only lose the owner, because they cannot be spawned again.  
  
### Fixed  
- Connections that died without a close frame were never detected.  
- Sending on a connection that just closed threw exceptions.  
- WebGL sockets leaked their slot and could send while still connecting or already closing.  
- A long frame, such as loading a scene, timed out every player.  
  
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
  
[2.2.0] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/2.1.2...2.2.0  
[2.1.2] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/2.1.1...2.1.2  
[2.1.1] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/2.1.0...2.1.1  
[2.1.0] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/2.0.0...2.1.0  
[2.0.0] https://github.com/forcepusher/com.bananaparty.websocketrelay/compare/1.1.1...2.0.0  
