# DovahLink Host and Native Adapter Migration

**Status:** Active

This is the durable migration plan for the parallel `host/` and `adapter/`
replacement. The current remediation pass has completed its host-core work
through session ownership, trust persistence, and documentation closeout;
later work begins at the live-boundary implementation stages.

The plan is intentionally retained at `host/PLAN.md`, linked by the root
architecture and the host/adapter architecture records. It is not a temporary
scratch plan and it does not authorize implementation of a later stage by
itself.

The Stage 1–6 checkboxes below track the host/adapter engineering buildout:
architecture and contract lock, the standalone host core, the thin native
adapter and private IPC, the host client boundary and pairing, host-owned
state/publication/delivery, and real capture integration. Stage 7 and
Stage 8, formerly this plan's own conformance and cutover stages, are
superseded by `roadmap/03a-host-adapter-production-migration.md`'s Stage 3A
and retained below only as pointers to that authoritative document.

## Relationship to the product roadmap

This is a parallel engineering track, not a renumbering or silent replacement
of the product roadmap's numbered stages. `ROADMAP.md` remains the source of
product-stage order and status; this plan owns the host/adapter engineering
buildout in Stages 1–6. Completing a host/adapter stage does not close the
corresponding product-roadmap stage or activate the replacement in
production.

Activating the replacement in production and removing `bridge/` are governed
by `roadmap/03a-host-adapter-production-migration.md`'s Stage 3A, not by this
plan's own Stage 7/8: 3A.1 (Production Cutover) targets the released Stage 3
baseline, not the live-state work tracked by this plan's Stage 5/6, so
completing Stage 5/6 is not a cutover prerequisite. Stage 5 and Stage 6's
scope and acceptance criteria are the same engineering work as
`roadmap/04-live-state-synchronization-foundation.md`'s "Host/Adapter
continuation (post-3A)" section; they are recorded once, there, as ordinary
Product Stage 4 work rather than duplicated as an independent authoritative
plan here. `ROADMAP.md` and this plan must not carry two contradictory
cutover policies: this document defers to `ROADMAP.md`'s Stage 3A wherever
the two could disagree.

This document is a migration-engineering aid, not a second permanent
roadmap. Once Stage 3A.3 (Repository Normalization) completes, its remaining
durable content — the process-lifecycle and packaging target, the ownership
split, and any invariant not already captured in `ARCHITECTURE.md`,
`ai/context/host/architecture.md`, or `ai/context/adapter/architecture.md` —
is expected to be folded into those durable documents and this file retired,
per Stage 3A.3's acceptance criteria.

## Host-core remediation status

This branch's focused remediation sequence is complete:

- Step 1 — factory-reset security and atomicity.
- Step 2 — pairing semantics.
- Step 3 — trust-administration parity.
- Step 4 — adapter-generation and state-provenance races.
- Step 5 — session ownership and bounded admission.
- Step 6 — trust persistence hardening.
- Step 7 — architecture, migration-audit, and plan documentation closeout.

These remediation steps repair and lock the host-core foundation; they do not
replace the ordered migration stages below or authorize their later runtime
work.

This plan builds a new C# host and thin native C++ adapter beside the existing
`bridge/` implementation. The existing bridge remains the behavioral reference
until the replacement passes 3A.1's production-cutover acceptance criteria in
`roadmap/03a-host-adapter-production-migration.md`. It is not refactored as
part of this engineering buildout and is removed only by 3A.2 (Legacy Bridge
Removal), never by this plan directly.

The existing 4.1 protocol and 4.2 bridge implementation are compatibility
evidence while the replacement is built. Their semantic decisions may be
retained, simplified, or deliberately revised after the host boundary is
defined. The public SDK-to-host contract and the private host-to-adapter
contract are separate contracts; the public envelope is not reused as the
internal IPC message model.

## Ownership target

`host/` owns the client-facing and bridge-application behavior:

- WebSocket hosting and client session lifecycle.
- Protocol mapping for the Dart SDK and other conforming clients.
- Pairing, persistent trust, authentication, authorization, and revocation.
- Subscriptions, authoritative published state, revisions, recovery, and
  per-session bounded queues.
- Diagnostics, host availability, reconnect behavior, and host-side shutdown.

`adapter/` owns only the Skyrim boundary:

- SKSE loading, runtime compatibility, and native lifecycle callbacks.
- Synchronous game-thread reads and bounded capture handoff.
- Play-context transition notifications and Skyrim-facing pairing/admin
  notifications.
- A private, bounded IPC connection to the C# host, package-versioned rather than wire-versioned.

`bridge/` is frozen reference behavior during this work. No new production
feature is added there after this plan starts, except a maintainer-approved
compatibility or safety fix needed to keep the reference usable.

## Process lifecycle and packaging target

The Vortex-installable mod package owns both the standalone host executable and
the native adapter. When Skyrim loads the adapter, the adapter starts the
packaged host as a hidden external Windows process if it is not already running;
the adapter never embeds the CLR or loads host assemblies. The host opens its
private IPC listener, and the adapter connects to it through a bounded retry path
that never blocks game-thread work. The adapter requests graceful host shutdown
when Skyrim closes and supervises the host with an OS-backed parent-lifetime
mechanism so a crash or forced termination cannot leave an orphaned host. The
host does not load Skyrim/CommonLib code.

The host and adapter have independent process lifetimes. A host restart ends
client connections and creates a new host process lifetime; an adapter restart
creates a new adapter instance identity and requires fresh resynchronization.
The host may remain running while no adapter is connected. Shutdown must close
client sessions and the private IPC channel through one deterministic host
teardown path, while adapter shutdown remains safe if the host has already
gone away.

The OS process identifier is operational/diagnostic data only. It is not a
durable client identity, a public protocol identity, or a replacement for the
host-owned `sessionId`, the host-owned per-socket `ConnectionId`, the
adapter-owned `adapterInstanceId`, or the adapter-reported `playContextId`.

## Stage 1: Architecture and Contract Lock
- [x] Complete

**Scope:**
Record the process model, ownership boundaries, lifecycle relationship, public
SDK-to-host contract, private host-to-adapter contract, restart behavior, and
cutover policy before building the replacement. Audit 4.1 and 4.2 semantics
against the new boundary and identify which decisions are preserved, changed,
or intentionally deferred.

**Acceptance criteria:**

- The C# host is explicitly out-of-process; embedding the CLR in Skyrim is not
  part of the design.
- The host is the sole new owner of pairing, trust, authentication, WebSocket
  sessions, publication, revisions, recovery, and client queues.
- The adapter is the sole new owner of Skyrim/CommonLib access and game-thread
  capture.
- The public SDK-to-host contract is distinct from the private host-to-adapter
  contract.
- The private IPC contract defines message framing, package-version ownership,
  size limits, authentication/ACL expectations, backpressure, host loss,
  adapter loss, and current-state resynchronization.
- Every retained 4.1/4.2 semantic decision has an owner in the new design, and
  every rejected decision has a recorded reason.
- The final cutover and deletion conditions for `bridge/` are explicit.

**Not in scope:** implementation of the host, adapter, IPC, or protocol changes.

**Depends on:** None

**Notes:** This stage is the decision gate for all later stages.

## Stage 2: Standalone C# Host Core
- [x] Complete

**Scope:**
Create the host as a standalone .NET process with typed application services
and no dependency on Skyrim or the existing C++ bridge. Build its domain and
service boundaries around fake adapter input so pairing, trust, session state,
state publication, and recovery can be tested before process integration.

**Acceptance criteria:**

- Host behavior-bearing services use explicit contracts and constructor
  injection.
- Host domain values do not depend on C++ types or the old bridge
  implementation.
- Pairing, trust, authentication, session identity, play-context identity,
  adapter instance identity, and host process lifetime have independent
  lifecycle ownership.
- Host restart and adapter restart have defined, testable state-recovery
  behavior.
- Host tests cover success, rejection, expiry, revocation, reset, stale
  session, and unavailable-adapter paths.

**Not in scope:** real WebSocket hosting, real IPC, or deleting `bridge/`.

**Depends on:** Stage 1

**Notes:** The existing protocol fixtures may be consumed as compatibility
test data, but the old C++ codec is not linked or reused.

## Stage 3: Thin Native Adapter and Private IPC
- [x] Complete

**Scope:**
Create the new C++ SKSE adapter and the private host-to-adapter channel. Keep
game-thread work bounded and synchronous where Skyrim requires it, while
moving all client-facing behavior out of the native process. The adapter must
remain safe when the host is unavailable, restarting, or shutting down.

**Acceptance criteria:**

- The adapter builds independently from the old `bridge/` implementation.
- Skyrim/CommonLib headers and runtime objects do not cross into host code.
- Game-thread callbacks perform only approved reads, validation, owned capture,
  and bounded handoff.
- The IPC channel uses owned messages, explicit limits, cancellation, and
  deterministic shutdown.
- Host loss cannot block or crash Skyrim capture.
- Adapter loss is observable by the host and results in a controlled
  unavailable/resynchronization state.
- The adapter can answer a host resynchronization request through an approved
  game-thread path.
- The adapter starts or safely adopts only the host process belonging to its
  Skyrim lifetime, launches it without a visible window, retries without
  blocking game-thread work, and cleans it up after orderly or forced Skyrim
  termination.

**Not in scope:** client WebSockets, pairing policy, or public protocol
cutover.

**Depends on:** Stage 1 and Stage 2

**Notes:** A small native handoff queue is expected; moving WebSocket queues to
the host does not permit blocking a Skyrim callback on IPC availability.
"Deterministic shutdown" holds for a live connection -- generation-guarded
rejection and cancellation make an active session's teardown deterministic.
Skyrim's own orderly close cannot run that same ordered sequence: `DllMain`'s
`DLL_PROCESS_DETACH` runs under the Windows loader lock, so that path only
makes the existing non-blocking host-shutdown signal, and adapter-side
threads, sockets, and handles are reclaimed by OS process termination rather
than an explicit join, per `ai/context/adapter/architecture.md`'s "Restart
behavior".

## Stage 4: Host Client Boundary and Pairing
- [x] Complete

**Scope:**
Implement the host-facing client connection boundary. The C# host accepts
loopback WebSocket clients, validates typed messages, owns pairing and trust,
and forwards only the narrow Skyrim-facing notifications or commands required
through the adapter channel.

**Acceptance criteria:**

- The host owns the WebSocket listener, handshake, read loop, write loop,
  timeout, cancellation, and connection teardown.
- Pairing, trusted-device credentials, developer authentication, revocation,
  blocking, reset, and session invalidation are host-owned.
- The host does not expose adapter IPC as a public client endpoint.
- Public messages are bounded, validated, authorized, and rejected before
  reaching adapter code.
- The host preserves the required fresh session and play-context identity
  semantics.
- Independent C# tests cover concurrent reads/writes, cancellation, peer
  failure, timeout, reconnect, and administrative invalidation.

**Not in scope:** live state publication or final removal of the old bridge.

**Depends on:** Stage 2 and Stage 3

## Stage 5: Host State, Publication, and Bounded Delivery
- [ ] Complete

**Relocated.** This stage's scope and acceptance criteria — host-owned
authoritative state, subscriptions, revisions, publication ordering,
recovery, per-session bounded queues, Snapshot/Event behavior, reserved
control capacity, and serialized WebSocket writing — are recorded in
`roadmap/04-live-state-synchronization-foundation.md`'s "Host/Adapter
continuation (post-3A)" section, "Host-owned state, publication, and bounded
delivery" subsection. This is ordinary Product Stage 4 engineering work, not
a prerequisite for Stage 3A's production cutover.

**Depends on:** Stage 4

## Stage 6: Real Capture and Host Integration
- [ ] Complete

**Relocated.** This stage's scope and acceptance criteria — connecting the
real adapter capture stream and play-context lifecycle to the host state
pipeline, and the narrow first production state slice (level-up event, fast
vitals, medium XP, slow gold) — are recorded in
`roadmap/04-live-state-synchronization-foundation.md`'s "Host/Adapter
continuation (post-3A)" section, "Real capture and host integration"
subsection. This is ordinary Product Stage 4 engineering work, not a
prerequisite for Stage 3A's production cutover.

**Depends on:** Stage 3 and Stage 5

## Stage 7: Compatibility, Conformance, and Cutover Readiness
- [ ] Complete

**Superseded.** Production cutover is now tracked as 3A.1 — Host/Adapter
Production Cutover in `roadmap/03a-host-adapter-production-migration.md`.
3A.1's compatibility target is the released Stage 3 baseline, not the
live-state work this stage previously depended on (the old Stage 6 above);
that live-state work is ordinary Stage 4 engineering and is not a cutover
prerequisite. See 3A.1's acceptance criteria for the current, authoritative
conformance scope: Stage 3 pairing/trust/authentication/reconnect/
administration/lifecycle/security parity, the ported `bAlwaysActive`/
`bAchievementCompat` runtime-compatibility behavior, a single active
`DovahLinkAdmin` implementation, and real end-to-end validation of launch,
pairing, reconnect, administration, and shutdown/orphan cleanup.

## Stage 8: Final Cutover and Removal
- [ ] Complete

**Superseded.** Cutover and removal are now tracked as 3A.1 — Host/Adapter
Production Cutover and 3A.2 — Legacy Bridge Removal in
`roadmap/03a-host-adapter-production-migration.md`. See that document for the
current, authoritative acceptance criteria, including the single active
`DovahLinkAdmin` implementation, `bridge/` deletion and CI/build/packaging
wiring removal, and relocating `integration/private-ipc-limits.json` before
removing legacy integration infrastructure.
