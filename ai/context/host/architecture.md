# Host architecture

These conventions apply to `host/`, the standalone C# process that owns the client-facing and
application behavior of DovahLink's production implementation. This document records Stage 1
("Architecture and Contract Lock") decisions: ownership boundaries, process lifecycle, and restart
behavior. It does not define concrete services, classes, or wire formats -- that implementation
work belongs to Stage 2 onward and must not be read back into this record.

## Process boundary

The host is explicitly **out-of-process**: a standalone .NET process with no dependency on Skyrim
or CommonLib. Embedding the CLR inside Skyrim is not part of the design. The host communicates with
the native adapter only through the private IPC channel defined in "Host-to-adapter IPC contract"
below.

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

The Host also owns a persistent public `hostId`: a DovahLink-generated UUID stored at
`%LOCALAPPDATA%/DovahLink/host/host-id.dat`, separately from the trust store. It survives Host,
Adapter, Skyrim, and Windows restarts, computer renames, and endpoint changes. Trust reset, client
revocation, blocking, and Factory Reset leave it unchanged. Removing or recreating this dedicated
identity file is the deliberate identity reset and causes a new ID to be generated. Corrupt identity
storage fails startup rather than silently changing the Host's identity.

On each Host startup, `hostName` is read from the operating-system computer name (`Environment.MachineName`).
It is mutable display metadata, bounded to the existing 64-byte display-name limit; if it cannot be
read or is invalid, the Host uses `Skyrim PC` and continues starting. `endpoint` remains the current
network location. None of these values substitutes for `stateAuthorityId`, `adapterInstanceId`,
`sessionId`, `clientId`, Steam ID, or a hardware/device ID.

## Host cryptographic identity key

Beside `host-id.dat`, the Host has a persistent ECDSA P-256 identity key for its installation,
implemented as a dormant pre-alpha foundation under the P10 authorization recorded in
`ai/context/security/identity-and-transport.md`. The private key is a persisted, per-Windows-user key
in the Microsoft Software Key Storage Provider, named from the `hostId` and created with export
disabled. It is never exported, serialized, logged, or returned; that export policy guards against
ordinary key export, not against code already running as the same Windows user. A public-key record
beside the identity file holds only the key's exact DER SubjectPublicKeyInfo, whose fingerprint is the
unpadded base64url SHA-256 of those bytes.

The first load for a `hostId` creates the key and then writes its record atomically. Every later
load verifies the persisted key against that record. A recorded key that is missing, inaccessible,
not a P-256 key with export disabled, or different from the record, and a corrupt record, all fail
closed: the Host never regenerates or silently replaces a key its record says existed. A deliberate
identity reset (a new `hostId`) retires the previous ID's key and record together on the next load.
Host processes of one Windows user serialize key loads through a per-user lock file, because the
provider does not reliably refuse two processes creating the same key name at once.

The running Host does not load this key yet: no startup, handshake, discovery, pairing, or trust path
uses it, and nothing publishes its public key or fingerprint. Activation belongs to
[Stage 5A](../../../roadmap/05a-windows-sas-integration-validation.md#sas-pairing-activation).

## Dormant `sas-pairing` integration foundation

The Host's pre-alpha [`sas-pairing`](https://github.com/Soneka96/sas-pairing) integration is split by
dependency so the running Host cannot compose it by accident:

- `DovahLink.Host` owns everything that does not need `sas-pairing`: the identity key above, the
  RFC 9562 UUID encoding, and the encoders for the Host's DovahLink Bootstrap frame and pairing
  authority scope, which reproduce `sas-pairing`'s frozen DovahLink mapping vectors byte for byte. It
  references neither `sas-pairing` nor the integration project.
- `DovahLink.Host.PairingCeremony` is a class library that references the pinned `SasPairing` .NET
  package and not `DovahLink.Host`, so it cannot reach trust, KnownDevice, Pair/Reject/Block, bearer,
  or session code. Its public boundary carries only detached values and bytes, never package objects,
  sockets, or native handles. One dedicated owner thread holds the process's single `sas-pairing`
  runtime, authority, host, and loopback listener, and is the only code that drives the native host or
  approves a SAS; other threads post commands through a bounded queue and never wait on a drive. A SAS
  decision takes effect only for the exact ceremony the owner has presented, and no production code
  submits one. The library validates a local result into detached ceremony evidence by comparing the peer's
  whole Bootstrap frame with the expected frame. One Host installation's authority scope has a single
  owner across processes of one Windows user; a second process fails closed as authority-unavailable.
- `DovahLink.Host.PairingCeremony.TestPeer` is test infrastructure: a separate process that plays a
  real `sas-pairing` Initiator or holds an authority scope for the real-native tests. It is never
  packaged, published, or referenced by the product.
- `DovahLink.Host.Tests` alone references the integration library and builds the test peer.

`host/sas-pairing-dependency.json` pins the dependency to one `sas-pairing` commit, package version,
target, and native ABI. `tooling/sas_pairing_dependency.py` builds that pinned source into the ignored
`out/sas-pairing/` folder and checks it with `sas-pairing`'s own package and export verifiers;
`host/nuget.config` restores `SasPairing` from that local feed only.

The running Host composes none of this. It binds no `sas-pairing` listener, runs no ceremony, displays
or approves no SAS, produces no Pair/Reject/Block decision, and writes no trust from a result, and the
packaged Host contains no `sas-pairing` assembly or native library. The six-digit pairing flow and
bearer reconnect remain the product behavior. Stage 5A activates the foundation, limited to Windows loopback, by adding the
`DovahLink.Host` reference to `DovahLink.Host.PairingCeremony` and the production lifecycle,
presentation, and authorization integration around it.

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

The public loopback listener also serves a bounded, sessionless `GET /.well-known/dovahlink`
response for local discovery and Known Host presence. It contains only the stable Host ID, current
Host name, and Host release version. Those fields are an unauthenticated Host claim, not proof of
identity. The listener's raw public-connection bound is the configured active-session capacity plus
a separately fixed allowance for pre-session connections; `SessionRegistry` remains the sole owner
of authenticated session admission and its configured `MaxActiveSessions` limit.

## Per-connection capability boundary

Application, session, and domain code above the transport never resolves which live connection it
is acting on through a global or listener-owned "current connection" lookup. Any per-connection
behavior -- sending a response, requesting that connection's termination, or anything else scoped to
one accepted client -- is reached only through an explicit capability tied to that exact
connection's own lifetime, handed to the caller by the transport itself. A capability that outlives
its connection must never observably act on a later, unrelated connection. This is an ownership
invariant, not a concrete interface or type shape; the transport layer choosing how to satisfy it is
implementation work for the stage that introduces the first such capability.

## Boundary against Skyrim

The host does not read Skyrim/CommonLib state, does not perform game-thread work, and does not
depend on native runtime types. Every value the host publishes as authoritative state originates
from a capture the adapter sent over the private IPC channel; the host never fabricates or infers
game state on its own. See `ai/context/adapter/architecture.md` for the adapter's matching
boundary against client-facing behavior.

## State-domain boundaries

A public state area is the smallest independently authoritative domain, not automatically one
scalar field and not an entire feature. Before splitting or combining fields, evaluate whether they
share a capture source and observation instant, cadence, authority, availability, revision lifecycle,
delivery mode, and recovery semantics. Fields that share those properties form one typed state value
and one revision; fields with meaningfully different properties remain separate areas. For example,
current and effective maximum Health, Magicka, and Stamina belong to one `character_vitals`
Snapshot when they are read together in one coherent Fast capture. XP remains separate from
Vitals, and Level remains separate when its Event plus Snapshot-baseline behavior differs.

This rule does not combine unrelated state into a whole-character object. Each area must remain
independently authoritative and recoverable under its own lifecycle.

For each connected client, a play-context or state-authority boundary is an ordered state operation:
invalidate accepted-area recovery state, discard pending Data-lane Events/Snapshots and deferred
Snapshots while releasing their reservations, then queue an unavailable revision-zero Snapshot for
each accepted area before forwarding new-identity state. The single writer may finish a frame it has
already dequeued before the reset; no other old queued state may follow a reset. A synthetic
revision-zero baseline does not advance the authoritative state store, so the first actual capture in
a new play context remains revision one. This operation ranges over registered and accepted areas;
it has no per-domain reset path.

## Authoritative state ownership

There is exactly one mutable Host owner of the authoritative current state and revision for each
live state area: the Host-lifetime `AuthoritativeStateStore`. It keeps one record per area holding
the typed value, the revision, the capture provenance (adapter instance, connection generation,
play context and its generation, and `stateAuthorityId`), the capture time, and the serialized
Snapshot a client is replayed. A value, its revision, and its replay Snapshot are committed together
under one lock, so they cannot disagree. No other Host type keeps a second copy of current state or
a revision counter.

- The public publication layer (`IStatePublicationFeed`, implemented by `StatePublicationFeed`) is a
  stateless view over that owner. It holds no snapshot, value, or revision of its own, and
  `PublicStateSubscription` reads only through it.
- An area's typed value is decoded by the capture handler, never by the store, and an area's value
  type is fixed by its first write; a later write with another type fails closed.
- An availability transition makes every record non-replayable and advances the revision of each
  populated area in the current play context once. The typed value is retained so a resynchronization
  baseline equal to it restores currentness under fresh provenance without a value revision or a
  change notification. A play-context transition drops the previous context's records, so the new
  context restarts at revision one.
- Reads and writes validate adapter availability and resynchronization state, adapter instance and
  connection generation, play context and generation, and `stateAuthorityId`. A capture stamped with
  an identity that is no longer current is rejected, never stored under the newer identity.
- A resynchronization baseline counts toward its transaction only after the store has committed it as
  replayable current state, and completion is observable before that baseline's change notification.
  The store runs one caller-supplied step at exactly that point, for baselines only, because only a
  baseline can complete a transaction.
- The store raises `SnapshotChanged` and `EventOccurred` synchronously under its ordering lock, in
  commit order and after the area's authoritative, replayable state is committed. A subscriber that
  reads `TryGetSnapshot` therefore sees state at least as new as the notification. Each subscriber's
  failure is contained individually. Subscribers run synchronously on the raising thread and must
  not block or call back into state application.
- `SnapshotAvailabilityChanged` is an availability hint emitted when the adapter availability
  tracker reports that resynchronization has completed; it is not an authoritative state publication.
  The tracker raises that event outside its own lock. A completing baseline can trigger it while the
  store's ordering lock happens to be held, but that is not a general contract. Consumers must always
  call `TryGetSnapshot` and re-check current state. Because a completing baseline can wake a waiting
  subscription before its own change notification arrives, `PublicStateSubscription` ignores a
  Snapshot at or below the revision it has already delivered for a live area.

To diagnose a missing value, read `AuthoritativeStateStore.TryGetSnapshot` for the area. If it
reports unavailable, the Host does not currently consider the area authoritative and replayable, so
investigate the adapter, capture, or resynchronization. If it reports a Snapshot, the Host holds
current state, so investigate the subscription, the SDK, or the app.

## Public contract ownership

The public SDK-to-host contract and the private host-to-adapter contract are separate contracts;
the public envelope is not reused as the internal IPC message model. `protocol/` remains the sole
canonical language-neutral contract between the host and its clients (Dart SDK and any other
conforming client), per `ARCHITECTURE.md`'s "Protocol" boundary and `ai/context/protocol/`. Stage 1
does not change that ownership or the schema itself -- it only moves the implementing process from
the retired native plugin to `host/`. The private host-to-adapter IPC contract is recorded separately, in
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
- **Adapter restart** means a Skyrim process restart; live SKSE plugin unload/reload is not a
  supported lifecycle boundary, per `ARCHITECTURE.md`'s runtime and identity model. It creates a
  new `adapterInstanceId`. The host observes the IPC connection drop and reconnect and
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
  again. This is the moment the public `stateAuthorityId` identifier must rotate, per
  `ARCHITECTURE.md`'s "Runtime and identity model" and
  `plans/documentation-and-composition-normalization/01.3a-public-vocabulary-and-identity-semantics.md`
  Section C -- the loss is the continuity break, not the resync that follows it.
- **Current-state resynchronization:** after either side reconnects, the adapter answers a host
  resynchronization request through an approved game-thread path and the host treats the result as
  a fresh authoritative baseline, not an incremental update layered on stale state -- the same
  Snapshot-establishes-a-new-baseline rule the public transport already uses
  (`ai/context/protocol/security.md`'s "Input limits" queue policy). This establishes the fresh
  baseline *under* the `stateAuthorityId` value already rotated at the preceding loss -- it does not
  rotate the value a second time, whether this resync resolves via the same `adapterInstanceId`
  reconnecting or a new one binding.

Concrete wire shapes, message types, and the exact version/limit numbers are Stage 3 implementation
work; this section fixes the decisions those numbers must satisfy.

## Not in scope for Stage 1

No concrete host service, class, dependency-injection shape, or wire message is defined here.
Stage 2 ("Standalone C# Host Core") designs the host's internal service boundaries against this
ownership and lifecycle record; Stage 3 builds the private IPC channel this document's contract
section constrains.
