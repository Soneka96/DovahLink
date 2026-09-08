# Adapter-host private IPC

This directory holds shared fixtures for the private, local IPC channel between the native
Adapter and the C# Host. It is not part of the public client protocol: `protocol/` remains the
sole canonical contract between the Host and its clients (Dart SDK and any other conforming
client), per `ARCHITECTURE.md`'s "Protocol" boundary. The private Host-to-Adapter contract itself
-- framing, package ownership, size limits, authentication, backpressure, and loss behavior -- is
recorded in `ai/context/host/architecture.md`'s "Host-to-adapter IPC contract".

## Contents

- `fixtures/private-ipc-limits.json` -- the shared inbound message-rate limit both the adapter
  (native) and the host (C#) test against, so neither side's hardcoded constant can silently drift
  from the other's.
