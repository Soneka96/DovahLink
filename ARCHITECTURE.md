# Architecture

## Repository boundaries

The implementation is divided into explicit areas:

```text
app/          Flutter client
bridge/       native SKSE bridge (frozen reference; see "Host and native adapter migration" below)
host/         standalone C# host process: client-facing and bridge-application behavior
adapter/      thin native SKSE adapter: the Skyrim boundary only
protocol/     canonical cross-side schemas and shared fixtures
sdk/          reusable supported client SDK implementations
integration/  cross-area tests and scenarios
ai/context/   AI development conventions
```

These are ownership boundaries, not folders to pre-create. Add an area when its first real file is needed. Protocol schemas and shared fixtures belong only in `protocol/`; clients, the host, and the adapter consume them but do not redefine them. The intended first SDK implementation is `sdk/dart/dovahlink_client/`, added when the Dart Client SDK Foundation phase begins; see `sdk/README.md` for its current planned status.

## Host and native adapter migration

`host/` and `adapter/` are replacing `bridge/`'s client-facing and native responsibilities,
per `host/PLAN.md`. The C# host is explicitly **out-of-process**: it runs as its own OS process
communicating with the native adapter over a private IPC channel; embedding the CLR inside Skyrim
is not part of the design.

Ownership split for the replacement:

- `host/` owns WebSocket hosting and client session lifecycle; protocol mapping for the Dart SDK
  and other conforming clients; pairing, persistent trust, authentication, authorization, and
  revocation; subscriptions, authoritative published state, revisions, recovery, and per-session
  bounded queues; diagnostics, host availability, reconnect behavior, and host-side shutdown. See
  `ai/context/host/architecture.md`.
- `adapter/` owns only the Skyrim boundary: SKSE loading, runtime compatibility, and native
  lifecycle callbacks; synchronous game-thread reads and bounded capture handoff; play-context
  transition notifications and Skyrim-facing pairing/admin notifications; and a private, bounded,
  versioned IPC connection to the host. See `ai/context/adapter/architecture.md`.

`bridge/` is frozen reference behavior during this migration: no new production feature is added
there except a maintainer-approved compatibility or safety fix needed to keep the reference usable.
It is not refactored as part of the migration and is removed only after the replacement passes the
full conformance and runtime validation matrix, per "Bridge migration and cutover" below.

The package installs the standalone C# host and native adapter together. Startup
brings up the host's private IPC listener before Skyrim loads the adapter; the
adapter then connects to the host. The two are independent OS processes: host
failure must not crash or block the adapter, and adapter absence is a valid host
state. OS process identifiers are diagnostic only and are not DovahLink
identities. The durable migration plan is [host/PLAN.md](host/PLAN.md).

## Bridge migration and cutover

Final cutover to `host/`/`adapter/` and removal of `bridge/` follow explicit gate conditions,
recorded here as durable project policy that `host/PLAN.md`'s own Stage 7 and Stage 8 acceptance
criteria implement:

- `bridge/` remains the production implementation, and every production path continues to link,
  launch, and depend on it, until the replacement passes the complete conformance, security,
  pairing, reconnect, state, queue, failure, and runtime validation matrix described in
  `host/PLAN.md`'s Stage 7. A failed or incomplete gate leaves `bridge/` as the production
  implementation; it does not fall back to a partial cutover.
- Every retained 4.1/4.2 semantic decision recorded in `ai/context/host/migration-audit.md` must be
  proven equivalent (or an approved, documented difference) at the live boundary before the gate is
  considered passed, not merely implemented in isolation.
- Once the gate passes, production packaging starts the C# host and installs the native adapter
  with the required lifecycle relationship; no production path may link, launch, or depend on the
  old `bridge/` tree.
- Deleting `bridge/` and its build/test wiring is a separately reviewable change from the cutover
  itself, opened only after the conformance gate has already passed and the replacement is already
  the production implementation -- deletion never happens in the same change that first proves the
  replacement works.

## Target shape

```text
┌─────────────┐     ┌──────────────┐   private IPC   ┌──────────────┐   public protocol   ┌──────────────┐
│ Skyrim      │────▶│ thin native  │────────────────▶│ DovahLink    │────────────────────▶│ Dart Client  │
│ game state  │     │ adapter      │                 │ host process │                     │ / other     │
└─────────────┘     └──────────────┘                 └──────────────┘                     └──────┬───────┘
                                                                                                  │
                                                                                      ┌───────────┴───────────┐
                                                                                      │                       │
                                                                                ┌─────▼─────┐           ┌─────▼─────┐
                                                                                │ Desktop   │           │ Mobile    │
                                                                                │ client    │           │ client    │
                                                                                └───────────┘           └───────────┘
```

## Boundaries

### Skyrim adapter

Reads the supported game state and hands bounded, typed captures to the host over private IPC. It should not own presentation, client sessions, pairing policy, or device-specific behavior.

SkyrimWebSocket and `bridge/` are historical references, not architectures for DovahLink to reproduce from scratch. The adapter owns only the Skyrim boundary; the host owns application behavior and public transport. Reuse does not make reference internals or wire formats canonical: the DovahLink protocol and private IPC contracts remain separate and authoritative at their respective boundaries.

### Protocol

Defines the canonical connection, pairing, capability, state, and error contract between the host and clients. It is the public seam between product processes, not a third implementation layer. The adapter's private IPC contract is separate.

SKSE owns native response and application types. Flutter owns client models. Both map to and from the protocol contract; neither side's internal types become the contract.

### SDK

Implements the canonical contract for Dart consumers and is not a second protocol authority: it maps the wire contract into typed client/domain models without those models becoming the contract itself. The official Flutter app is the SDK's first production consumer; after the Dart Client SDK Foundation phase (`roadmap/05-dart-client-sdk-foundation.md`), normal DovahLink communication from the app goes through the SDK rather than through app-private transport, compatibility, authentication, pairing, reconnect, or session code. See `ai/context/sdk/` for SDK-specific conventions and `sdk/README.md` for its current planned status.

### Clients

Render companion views and manage local layout preferences. A client should remain useful when optional data is unavailable.

## Initial technical decisions

- The host-to-client contract should be defined before multiple clients are built.
- The protocol contract is the source of truth for cross-side messages; Flutter and SKSE adapters must not silently invent incompatible fields.
- The official Flutter application is one client of the canonical protocol. It receives no
  undocumented protocol behavior or privileged access that another conforming client could not use.
- Protocol messages remain presentation-independent. Screens, dashboard modules, orientation,
  widget layout, and other client UI concepts do not belong in the public host/client contract.
- The wire contract is defined by `protocol/schema/README.md`; transport framing remains outside
  the architecture contract.
- Transport exposure, pairing, authentication, and input limits are defined by
  `ai/context/protocol/security.md`.

## Runtime and identity model

One live Skyrim process owns one DovahLink adapter instance connected to one host process. A host may
eventually serve multiple concurrent clients, and one machine may host multiple adapter/host pairs when multiple supported
Skyrim processes exist. Transport location is not identity: an address, port, hostname, or transport
path locates an endpoint but must not become the durable identity of a bridge, play context, client,
or connection.

For historical compatibility, the old bridge behavior is recorded here.
A bridge restart creates a new identity in the old bridge implementation; in
the replacement this means an adapter restart creates a new `adapterInstanceId`.
The target architecture distinguishes five
identifiers across four lifetimes:
the transport `ConnectionId` and authenticated `sessionId` share the per-socket
lifetime.

- `adapterInstanceId` identifies one running adapter/plugin lifetime. An adapter restart creates a
  new identity. The host's OS process lifetime is separate and has no public identity. The historical
  `bridgeInstanceId` name is retained only in frozen-reference compatibility records.
- `playContextId` identifies the currently loaded authoritative play context. It changes whenever
  state from the previous loaded game must no longer be accepted as current.
- `clientId` identifies one paired client or device independently of any connection it opens.
- `ConnectionId` identifies one host-owned transport connection.
- `sessionId` identifies one authenticated socket session. It is valid only for that socket and is
  invalidated when the connection ends in the historical contract as well as in the replacement.

Each identifier must be created, validated, and invalidated at its own lifecycle boundary.
A client reconnect creates a new `sessionId` without silently changing its `clientId`; it also creates a new
`ConnectionId`; loading another save
creates a new `playContextId` without pretending that the adapter or host process restarted.

Persistent device trust is a separate concept layered on top of these four lifetimes, not a fifth
lifetime that replaces or reinterprets them. A paired client's local trust — the credential a client
presents to reconnect without repeating pairing — belongs to the Windows user profile running the
client and the host, and survives host, adapter, Skyrim, and Windows restarts. It does not change
`adapterInstanceId`'s per-restart identity, `playContextId`'s per-load identity, or `sessionId`'s
per-socket identity: a trusted client still authenticates into a fresh `sessionId` on every reconnect,
and a bridge restart still creates a new `bridgeInstanceId` in the frozen Bridge reference; in the replacement an adapter restart
creates a new `adapterInstanceId`. `ai/context/protocol/security.md` and
`roadmap/03-local-device-pairing-and-reconnection.md`'s Phase 3 owns the pairing, storage, and revocation design; this section only fixes where
persistent trust sits relative to the four identifiers above.

The frozen reference's historical trust wording remains explicit: persistent
trust belongs to the Windows user profile running the client and the Bridge,
and survives Bridge, Skyrim, and Windows restarts. The target owner is the
host process and its per-user persistence adapter.

### Session registry and delivery ownership

The host uses a bounded session registry rather than a singleton delivery architecture. The
current admission policy is one active client session, owned by the host's session registry,
so the host remains single-client until the multi-client phase. The registry shape is
collection-based: each authenticated session record owns its `sessionId`, client-specific
capabilities and subscriptions, outbound queue, recovery barriers, serialized writer, and
diagnostics.

Capture policy, cadence scheduling, authoritative state stores, and state-area revisions belong to
host/play-context scope and are shared across session records. A client is never given a second
Skyrim read merely because it connects, and a session disconnect cannot invalidate authoritative
state. When no session is connected, current authoritative state continues to update; reliable
Events are scoped to the authenticated session and are not replayed across sessions, while the next
session receives fresh current Snapshots. Stage 9 raises the admission capacity and adds fan-out and
independent slow-client recovery using this same ownership boundary.

## Authoritative state and revisions

Skyrim is the authoritative producer of live playthrough state. For each state area, the host owns
one authoritative state store for the active play context. Skyrim state is captured once and shared
with subscribed clients; adding a client must not repeat equivalent Skyrim reads for that client.

A state revision identifies a version of authoritative state within one state area and
`playContextId`. It advances only when that authoritative state changes. Sending or requesting
another snapshot does not advance the revision when the state is unchanged, and reconnecting does
not create a new authoritative revision merely because the socket session changed.

Clients use `playContextId` and the state-area revision together to reject stale state. `sessionId`
and `ConnectionId` prevent a client from accepting messages from an old or foreign socket. When the play context
changes, the host invalidates the previous context's state and establishes fresh authoritative
state before publication resumes.

`protocol/schema/README.md` carries this ownership as the current canonical wire contract; see
`roadmap/02-bridge-identity-and-authoritative-state.md`'s Bridge Identity and Authoritative State Foundation entry for adoption status across
the bridge and its clients. This ownership must not be implemented by silently reinterpreting
messages from the previously published experimental release, which is archived rather than a
supported compatibility target. The current pre-cutover contract has no independent runtime
protocol-generation number; compatibility with the frozen reference is identified by the DovahLink
Bridge/mod release version. The target host contract will use the host release version when its
public compatibility boundary is activated, per `ai/context/protocol/compatibility.md`.

## Live delivery and performance model

- Prefer native Skyrim/SKSE events whenever a trustworthy event exists; sample only values that
  genuinely require sampling.
- Treat sampling rates as maximum rates rather than mandatory send cadences, and publish only when
  authoritative state changes.
- Keep game-thread callbacks bounded, non-blocking, and limited to approved capture work. Network
  I/O, serialization, expensive transformation, caching, and presentation work run elsewhere.
- Use latest-value-wins coalescing for replaceable live state under pressure.
- Keep reliable events ordered within their defined delivery scope. Disconnect or resynchronize a
  slow client rather than allowing it to stall Skyrim or healthy clients.
- Keep heavy resources such as maps, tiles, artwork, and large reference datasets outside the
  high-rate live-state stream.
- Select queue capacities, sampling rates, and executor topology from profiling rather than fixing
  speculative values as permanent architecture.

## Reliability expectations

- Treat the game as an unreliable producer: values may be unavailable or delayed.
- Make connection state visible to the player.
- Reject an incompatible host/client or adapter/host combination clearly.
- Avoid allowing a stale value to look current.
- Keep the first client read-only.

## Future feature boundaries

### Map data

A future map should separate expensive, slow-changing resources from small playthrough updates:

- Base maps, tiles, marker artwork, and other heavy resources are versioned and cached by the client rather than repeatedly streamed by the host.
- Additional worldspaces, DLC maps, and mod-provided map resources are loaded only when needed.
- Player position, active markers, discovery state, and Skyrim's native cleared state are lightweight snapshots or events layered over those resources.
- A location is shown as cleared only when Skyrim provides that state; DovahLink must not invent a universal completion percentage for locations that are not clearable.

Route guidance must reuse a path calculated by Skyrim, initially by validating whether the route produced by the native Clairvoyance/Guide system can be exposed safely as a lightweight polyline for the active quest objective. DovahLink may render and refresh that route, but it must not own a pathfinding engine, extract and maintain a parallel navmesh database, or approximate an unavailable route as valid. If Skyrim does not expose a reliable route, route guidance is deferred. Arbitrary map-selected destinations remain separate future work unless Skyrim can accept such a target through an explicitly approved safety model.

This split belongs to a dedicated map feature and does not expand the connection proof.

### Item knowledge and LOTD

A future item-knowledge feature may combine a searchable, versioned knowledge source with read-only save and load-order state. Optional Legacy of the Dragonborn (LOTD) support may add acquisition guidance, source quests or locations, supported-mod context, and collected or displayed state when those values can be determined reliably.

The knowledge source belongs outside the live host stream; the host should expose only relevant game state. This is separate from the map, adapter validation, and first companion workflow, even if a later client links an item result to a map location.

### UI theming and adaptation

The Flutter client's Core UI Theme System defines its Skyrim-inspired native presentation through
shared tokens and components for fonts, colors, panels, icons, spacing, and animations. Dashboard
modules consume that theme boundary rather than defining their own visual constants.

The core theme must remain usable without inspecting the Skyrim installation or depending on a
particular UI mod. Optional Installed UI Detection may identify supported UI and font resources
when detection is reliable, and optional adapters may map explicitly supported themes such as
SkyUI, Nordic UI, and Dear Diary into the DovahLink theme boundary. A `dovahlink-theme.json` format
may provide the same boundary for compatible theme packs, but it is not part of the core theme and
must be specified and approved before implementation.

Detection and adapters may supply supported presentation values; they must not replace client behavior, dashboard structure, or protocol models. Missing, unsupported, or invalid resources fall back to the native DovahLink theme.

## Architectural non-goals

- No service layer before local connectivity is proven.
- The Dart Client SDK Foundation phase (`roadmap/05-dart-client-sdk-foundation.md`) intentionally introduces a shared client
  implementation before a second product client exists, replacing the earlier assumption that such
  an abstraction should wait for a second client: the reusable connection lifecycle, compatibility
  detection, authentication, pairing, reconnect, and state-identity behavior it consumes has grown
  substantial enough to deserve its own boundary, with the official app as its first consumer.
- No permanent protocol complexity for hypothetical features.
- No dependency on an installed Skyrim UI mod for the default client presentation.
