# Adapter architecture

These conventions apply to `adapter/`, the thin native SKSE plugin that is replacing `bridge/`'s
Skyrim-boundary responsibilities, per `host/PLAN.md`. This document records Stage 1 ("Architecture
and Contract Lock") decisions: ownership boundaries and restart behavior. It does not define
concrete classes, dependency edges, or wire formats -- that implementation work belongs to Stage 3
onward and must not be read back into this record.

## Technology boundary

The adapter is a native SKSE plugin, C++, built independently from the existing `bridge/`
implementation -- it does not link against or depend on `bridge/` code. It is the only new
component allowed to depend directly on CommonLib or Skyrim runtime types, the same rule
`ai/context/skse/architecture.md` already applies to `bridge/`'s game-state adapters. Skyrim/
CommonLib headers and runtime objects never cross into host code, matching
`ai/context/host/architecture.md`'s "Boundary against Skyrim".

## Ownership

The adapter is the sole new owner of:

- SKSE loading, runtime compatibility, and native lifecycle callbacks.
- Synchronous game-thread reads and bounded capture handoff.
- Play-context transition notifications and Skyrim-facing pairing/admin notifications.
- Starting and supervising the packaged host executable as an external, hidden process, including
  requesting graceful host shutdown when Skyrim closes.
- A private, bounded, versioned IPC connection to the host.

## Boundary against client-facing behavior

The adapter does not own WebSocket hosting, client session lifecycle, pairing/trust/authentication
decisions, protocol mapping to the Dart SDK, or any part of the public SDK-to-host contract --
those stay entirely on the host, per `ai/context/host/architecture.md`'s "Ownership". Starting the
external host process is lifecycle plumbing, not ownership of host application behavior; the
adapter never embeds the CLR, loads host assemblies, or passes untrusted shell command text. Where
the adapter surfaces a Skyrim-facing pairing or admin notification (for example displaying an
in-game pairing code), it only presents a value the host decided; it never makes a pairing, trust,
or authorization decision itself.

## Restart behavior

An adapter restart means a new Skyrim process restart and creates a new adapter instance identity,
the same relationship `ARCHITECTURE.md`'s existing `bridgeInstanceId` already describes for the
native process. The adapter is intentionally a process-lifetime module: live SKSE plugin
unload/reload while Skyrim remains running is not supported, because its worker threads and
registered callbacks must remain inside the loaded DLL. A restarted adapter starts or reuses only
the host process tied to that Skyrim lifetime, establishes a fresh IPC connection, and answers the
host's resynchronization request rather than assuming continuity, per
`ai/context/host/architecture.md`'s "Restart behavior".

On an orderly Skyrim close, `DllMain` can only make the non-blocking host shutdown signal. The
launched host remains protected by the OS Job Object until Skyrim exits, so forced or abrupt
process termination is the cleanup boundary rather than a blocking DLL-unload sequence.

Host loss while the adapter keeps running -- as opposed to the adapter's own restart -- is a
property of the IPC channel between them, not of the adapter's own lifecycle: the adapter must
remain safe (continue Skyrim capture, never block or crash) when the host is unavailable. That
behavior is recorded in `ai/context/host/architecture.md`'s "Host-to-adapter IPC contract". A
failed connection attempt before a session is established is also a host-loss signal for the
process-lifecycle supervisor, so discovery can re-run without waiting for a disconnect callback
that an unconnected socket cannot produce. The adapter keeps the same long-lived IPC connection
object and retargets it after a verified endpoint is found; the supervisor's discovery retry works
 alongside the connection's existing bounded transport retry, without creating a second transport
 connection object or restarting the connection lifecycle.

## Not in scope for Stage 1

No concrete adapter class, dependency-edge diagram, threading design, or wire message is defined
here. Stage 3 ("Thin Native Adapter and Private IPC") designs the adapter's internal shape against
this ownership and lifecycle record.
