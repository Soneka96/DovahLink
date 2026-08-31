# Host architecture

These conventions apply to `host/`, the standalone C# process that is replacing `bridge/`'s
client-facing and application behavior, per `host/PLAN.md`. This document records Stage 1
("Architecture and Contract Lock") decisions: ownership boundaries, process lifecycle, and restart
behavior. It does not define concrete services, classes, or wire formats -- that implementation
work belongs to Stage 2 onward and must not be read back into this record.

## Process boundary

The host is explicitly **out-of-process**: a standalone .NET process with no dependency on Skyrim,
CommonLib, or the existing C++ `bridge/` implementation. Embedding the CLR inside Skyrim is not
part of the design. The host communicates with the native adapter only through the private IPC
channel defined in "Host-to-adapter IPC contract" below.

## Startup, packaging, and process identity

The Vortex-installable mod package contains the standalone host executable and
the native adapter as separate components. When Skyrim loads the adapter, the
adapter starts the packaged host as a hidden external Windows process if it is
not already running, then the adapter connects to the host's private IPC
listener. The adapter may launch the external process, but it never embeds the
CLR or loads host assemblies, Skyrim runtime objects, or CommonLib code into the
other process. Adapter startup and IPC reconnect must not block game-thread work;
the adapter supervisor coordinates one bounded connection attempt at a time when
host startup takes time or the host is temporarily unavailable. A failed host
start leaves the adapter safe to run without a connected host, while a failed
adapter connection leaves the host in its valid adapter-unavailable state.

The host process belongs to one adapter/Skyrim lifetime. The adapter must not
blindly reuse a stale or duplicate host process: it starts the packaged host or
adopts an already-running process only when that process proves it belongs to
the current lifetime through the private channel. The normal adapter connection
is the only proof path; there is no separate discovery probe that participates
in the host session state machine. The exact ownership proof is private-IPC
implementation work. The adapter supervises the host with an
OS-backed parent-lifetime mechanism. On an orderly Skyrim shutdown, the adapter
requests the host's deterministic graceful teardown first; the OS-backed
mechanism is the fallback for crashes and forced termination. The host remains
responsible for closing client sessions, private IPC, and its own application
resources before exiting.

The host and adapter have independent OS process lifetimes. A host restart
ends all client sockets and their sessions, reloads persistent trust, and
starts with no authoritative live state until adapter resynchronization. An
adapter restart creates a fresh adapter instance identity and requires a new
private-IPC handshake and state baseline. Shutdown follows one deterministic
host teardown path for client sessions and private IPC; the adapter must remain
safe if the host disappears first.

The OS process identifier is operational diagnostic data only. It is not a
durable or public DovahLink identity. The host owns the per-client transport
`ConnectionId` and authenticated `sessionId`; the adapter connection has a
host-observed `adapterInstanceId`; and the adapter reports play-context
transitions through the private channel. None of those identities is derived
from a port, path, hostname, or OS process id.

## Ownership

The host is the sole new owner of:

- WebSocket hosting and client session lifecycle.
- Protocol mapping for the Dart SDK and other conforming clients, implementing the public
  SDK-to-host contract that `protocol/` and `ai/context/protocol/` already own (see "Public
  contract ownership" below).
- Pairing, persistent trust, authentication, authorization, and revocation.
- Subscriptions, authoritative published state, revisions, recovery, and per-session bounded
  queues.
- Diagnostics, host availability, and host-side shutdown. Adapter-side
  discovery and reconnect coordination remain on the native adapter.

## Boundary against Skyrim

The host does not read Skyrim/CommonLib state, does not perform game-thread work, and does not
depend on native runtime types. Every value the host publishes as authoritative state originates
from a capture the adapter sent over the private IPC channel; the host never fabricates or infers
game state on its own. See `ai/context/adapter/architecture.md` for the adapter's matching
boundary against client-facing behavior.

## Public contract ownership

The public SDK-to-host contract and the private host-to-adapter contract are separate contracts;
the public envelope is not reused as the internal IPC message model. `protocol/` remains the sole
canonical language-neutral contract between the host and its clients (Dart SDK and any other
conforming client), per `ARCHITECTURE.md`'s "Protocol" boundary and `ai/context/protocol/`. Stage 1
does not change that ownership or the schema itself -- it only moves the implementing process from
`bridge/` to `host/`. The private host-to-adapter IPC contract is recorded separately, in
"Host-to-adapter IPC contract" below.

## Restart behavior

The host and adapter are independent OS processes with independent lifetimes. The host may run
without an adapter connected -- that is a valid, observable "adapter unavailable" state, not a host
failure, and it must not prevent the host from serving already-connected clients whatever
non-adapter-dependent behavior remains available to them (for example rejecting new pairing/state
requests cleanly rather than hanging).

- **Host restart** creates a new host process lifetime. Every existing `sessionId` is already
  invalidated the moment its socket closes (per `ARCHITECTURE.md`'s per-socket `sessionId`
  lifetime), so a host restart ends every client session the same way any host shutdown does.
  Persistent trust survives a host restart: it is stored per-Windows-user-profile independently of
  any single process's lifetime, extending the existing persistent-trust policy across host, adapter,
  Skyrim, and Windows restarts. The
  host's in-memory authoritative published state and revisions do not survive a host restart -- they
  are not persisted, so a restarted host holds no authoritative state until it resynchronizes with
  the adapter and starts a fresh revision sequence for every affected state area.
- **Adapter restart** (an SKSE plugin reload or a Skyrim process restart) creates a new
  `adapterInstanceId`. The host observes the IPC connection drop and reconnect and
  treats any state associated with the previous adapter connection as stale until it is
  resynchronized; it never continues publishing the old connection's state as current across an
  adapter restart.

Stage 2 established defined, *testable* host-restart and adapter-restart state-recovery policy;
this section records the behavior its tests must prove, not the test design itself. Host-loss and
adapter-loss behavior *while both processes keep running* (as opposed to one
of them restarting) is recorded in "Host-to-adapter IPC contract" below, since that behavior is a
property of the channel between them, not of either process's own lifecycle.

## Host-to-adapter IPC contract

The adapter is the connecting side; the host is the private IPC channel's owning/listening side,
matching the plan's framing of "a private... connection to the C# host." This section records
Stage 1's decisions for that channel -- framing, package ownership, size limits, authentication/ACL,
backpressure, host loss, adapter loss, and current-state resynchronization. It is separate from the
public SDK-to-host contract per "Public contract ownership" above: the public envelope is never
reused as the internal IPC message model, and this section does not touch `protocol/`.

- **Framing and package ownership:** the channel carries host-and-adapter-owned messages only.
  Host and adapter are shipped as one atomic package, so the channel does not negotiate a protocol
  version. Peer ownership and Skyrim-lifetime proof establish that the connection belongs to the
  matching package; a mismatched or unauthorized peer fails closed with an actionable diagnostic
  rather than being interpreted.
- **Size limits:** the channel is bounded the same way the public transport already is (see
  `ai/context/protocol/security.md`'s "Input limits") -- explicit per-message size and rate limits,
  not an unbounded local pipe, because an unbounded channel would let a stalled host or adapter
  build unbounded memory on the other side.
- **Authentication/ACL:** the channel is local-machine-only, matching "Phase 1 exposure"'s loopback
  posture for the public transport. It does not need pairing or a persistent credential -- there is
  exactly one adapter and one host per running Skyrim process -- but it must reject a connection
  from any process other than the expected local adapter/host pair, the same fail-closed posture
  `ai/context/protocol/security.md` requires everywhere else.
- **Backpressure:** the adapter's game-thread capture must never block on IPC availability or
  channel fullness (see `ai/context/adapter/architecture.md`'s "Restart behavior"). A full channel
  drops or replaces capture the same way the existing bounded outbound queue already does for
  replaceable Snapshot state (`ai/context/protocol/security.md`'s queue policy), never by blocking
  the game thread.
- **Host loss:** the adapter continues Skyrim capture and its own bounded local handoff when the
  host is unavailable or the channel is down; it must not crash, block, or silently discard capture
  state it could otherwise still hand off once the channel recovers, within its own bounded
  capacity.
- **Adapter loss:** the host observes channel loss, marks adapter-sourced state unavailable rather
  than presenting stale values as current (matching `ARCHITECTURE.md`'s "Reliability expectations"),
  and requires a resynchronization handshake before publishing adapter-sourced state as current
  again.
- **Current-state resynchronization:** after either side reconnects, the adapter answers a host
  resynchronization request through an approved game-thread path and the host treats the result as
  a fresh authoritative baseline, not an incremental update layered on stale state -- the same
  Snapshot-establishes-a-new-baseline rule the public transport already uses
  (`ai/context/protocol/security.md`'s "Input limits" queue policy).

Concrete wire shapes, message types, and the exact version/limit numbers are Stage 3 implementation
work; this section fixes the decisions those numbers must satisfy.

## Not in scope for Stage 1

No concrete host service, class, dependency-injection shape, or wire message is defined here.
Stage 2 ("Standalone C# Host Core") designs the host's internal service boundaries against this
ownership and lifecycle record; Stage 3 builds the private IPC channel this document's contract
section constrains.
