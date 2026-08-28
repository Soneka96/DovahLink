# Architecture

## Repository boundaries

The future implementation is divided into explicit areas:

```text
app/          Flutter client
bridge/       native SKSE bridge
protocol/     canonical cross-side schemas and shared fixtures
sdk/          reusable supported client SDK implementations
integration/  cross-area tests and scenarios
ai/context/   AI development conventions
```

These are ownership boundaries, not folders to pre-create. Add an area when its first real file is needed. Protocol schemas and shared fixtures belong only in `protocol/`; client and bridge adapters consume them but do not redefine them. The intended first SDK implementation is `sdk/dart/dovahlink_client/`, added when the Dart Client SDK Foundation phase begins; see `sdk/README.md` for its current planned status.

## Target shape

```text
┌─────────────┐     ┌──────────────┐     ┌───────────────┐
│ Skyrim      │────▶│ Skyrim       │────▶│ DovahLink     │
│ game state  │     │ bridge       │     │ protocol      │
└─────────────┘     └──────────────┘     └───────┬───────┘
                                                  │
                                          ┌───────▼───────┐
                                          │ Dart Client   │
                                          │ SDK           │
                                          └───────┬───────┘
                                                  │
                                    ┌─────────────┴─────────────┐
                                    │                           │
                              ┌─────▼─────┐               ┌─────▼─────┐
                              │ Desktop   │               │ Mobile    │
                              │ client    │               │ client    │
                              └───────────┘               └───────────┘
```

## Boundaries

### Skyrim bridge

Reads the supported game state and exposes a small, versioned stream of events. It should not own presentation or device-specific behavior.

SkyrimWebSocket is the starting reference and bridge foundation, not a separate architecture for DovahLink to reproduce from scratch. The first bridge feature should identify the parts that can be reused, then adapt them behind DovahLink's game-state, application, protocol-mapping, and transport boundaries. Reuse does not make SkyrimWebSocket's internal types or wire format canonical: the DovahLink protocol remains the cross-side contract, and code that does not fit the runtime, security, lifecycle, or ownership rules should be replaced or left out deliberately.

### Protocol

Defines the canonical connection, pairing, capability, state, and error contract between the bridge and clients. It is the seam between the two sides of one product, not a third implementation layer.

SKSE owns native response and application types. Flutter owns client models. Both map to and from the protocol contract; neither side's internal types become the contract.

### SDK

Implements the canonical contract for Dart consumers and is not a second protocol authority: it maps the wire contract into typed client/domain models without those models becoming the contract itself. The official Flutter app is the SDK's first production consumer; after the Dart Client SDK Foundation phase (`roadmap/05-dart-client-sdk-foundation.md`), normal DovahLink communication from the app goes through the SDK rather than through app-private transport, compatibility, authentication, pairing, reconnect, or session code. See `ai/context/sdk/` for SDK-specific conventions and `sdk/README.md` for its current planned status.

### Clients

Render companion views and manage local layout preferences. A client should remain useful when optional data is unavailable.

## Initial technical decisions

- The bridge-to-client contract should be defined before multiple clients are built.
- The protocol contract is the source of truth for cross-side messages; Flutter and SKSE adapters must not silently invent incompatible fields.
- The official Flutter application is one client of the canonical protocol. It receives no
  undocumented protocol behavior or privileged access that another conforming client could not use.
- Protocol messages remain presentation-independent. Screens, dashboard modules, orientation,
  widget layout, and other client UI concepts do not belong in the bridge contract.
- The wire contract is defined by `protocol/schema/README.md`; transport framing remains outside
  the architecture contract.
- Transport exposure, pairing, authentication, and input limits are defined by
  `ai/context/protocol/security.md`.

## Runtime and identity model

One live Skyrim process owns one DovahLink bridge instance. A bridge may eventually serve multiple
concurrent clients, and one machine may host multiple bridge instances when multiple supported
Skyrim processes exist. Transport location is not identity: an address, port, hostname, or transport
path locates an endpoint but must not become the durable identity of a bridge, play context, client,
or connection.

The target architecture distinguishes four lifetimes:

- `bridgeInstanceId` identifies one running bridge/plugin lifetime. A bridge restart creates a new
  identity.
- `playContextId` identifies the currently loaded authoritative play context. It changes whenever
  state from the previous loaded game must no longer be accepted as current.
- `clientId` identifies one paired client or device independently of any connection it opens.
- `sessionId` identifies one authenticated socket session. It is valid only for that socket and is
  invalidated when the connection ends.

Each identifier must be created, validated, and invalidated at its own lifecycle boundary. A client
reconnect creates a new `sessionId` without silently changing its `clientId`; loading another save
creates a new `playContextId` without pretending that the bridge process restarted.

Persistent device trust is a separate concept layered on top of these four lifetimes, not a fifth
lifetime that replaces or reinterprets them. A paired client's local trust — the credential a client
presents to reconnect without repeating pairing — belongs to the Windows user profile running the
client and the Bridge, and survives Bridge, Skyrim, and Windows restarts. It does not change
`bridgeInstanceId`'s per-restart identity, `playContextId`'s per-load identity, or `sessionId`'s
per-socket identity: a trusted client still authenticates into a fresh `sessionId` on every reconnect,
and a bridge restart still creates a new `bridgeInstanceId`. `ai/context/protocol/security.md` and
`roadmap/03-local-device-pairing-and-reconnection.md`'s Phase 3 owns the pairing, storage, and revocation design; this section only fixes where
persistent trust sits relative to the four identifiers above.

### Session registry and delivery ownership

The Bridge uses a bounded session registry rather than a singleton delivery architecture. The
current admission policy is `kMaxConnectedClients = 1`, owned by `bridge/security/constants.hpp`,
so the Bridge remains single-client until the multi-client phase. The registry shape is still
collection-based: each authenticated session record owns its `sessionId`, client-specific
capabilities and subscriptions, outbound queue, recovery barriers, serialized writer, and
diagnostics.

Capture policy, cadence scheduling, authoritative state stores, and state-area revisions belong to
Bridge/play-context scope and are shared across session records. A client is never given a second
Skyrim read merely because it connects, and a session disconnect cannot invalidate authoritative
state. When no session is connected, current authoritative state continues to update; reliable
Events are scoped to the authenticated session and are not replayed across sessions, while the next
session receives fresh current Snapshots. Stage 9 raises the admission capacity and adds fan-out and
independent slow-client recovery using this same ownership boundary.

## Authoritative state and revisions

Skyrim is the authoritative producer of live playthrough state. For each state area, the bridge owns
one authoritative state store for the active play context. Skyrim state is captured once and shared
with subscribed clients; adding a client must not repeat equivalent Skyrim reads for that client.

A state revision identifies a version of authoritative state within one state area and
`playContextId`. It advances only when that authoritative state changes. Sending or requesting
another snapshot does not advance the revision when the state is unchanged, and reconnecting does
not create a new authoritative revision merely because the socket session changed.

Clients use `playContextId` and the state-area revision together to reject stale state. `sessionId`
still prevents a client from accepting messages from an old or foreign socket. When the play context
changes, the bridge invalidates the previous context's state and establishes fresh authoritative
state before publication resumes.

`protocol/schema/README.md` carries this ownership as the current canonical wire contract; see
`roadmap/02-bridge-identity-and-authoritative-state.md`'s Bridge Identity and Authoritative State Foundation entry for adoption status across
the bridge and its clients. This ownership must not be implemented by silently reinterpreting
messages from the previously published experimental release, which is archived rather than a
supported compatibility target. The current contract has no independent runtime protocol-generation
number of its own; compatibility with it is identified by the DovahLink Bridge/mod release version,
per `ai/context/protocol/compatibility.md`.

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
- Reject an incompatible Bridge/client combination clearly.
- Avoid allowing a stale value to look current.
- Keep the first client read-only.

## Future feature boundaries

### Map data

A future map should separate expensive, slow-changing resources from small playthrough updates:

- Base maps, tiles, marker artwork, and other heavy resources are versioned and cached by the client rather than repeatedly streamed by the bridge.
- Additional worldspaces, DLC maps, and mod-provided map resources are loaded only when needed.
- Player position, active markers, discovery state, and Skyrim's native cleared state are lightweight snapshots or events layered over those resources.
- A location is shown as cleared only when Skyrim provides that state; DovahLink must not invent a universal completion percentage for locations that are not clearable.

Route guidance must reuse a path calculated by Skyrim, initially by validating whether the route produced by the native Clairvoyance/Guide system can be exposed safely as a lightweight polyline for the active quest objective. DovahLink may render and refresh that route, but it must not own a pathfinding engine, extract and maintain a parallel navmesh database, or approximate an unavailable route as valid. If Skyrim does not expose a reliable route, route guidance is deferred. Arbitrary map-selected destinations remain separate future work unless Skyrim can accept such a target through an explicitly approved safety model.

This split belongs to a dedicated map feature and does not expand the connection proof.

### Item knowledge and LOTD

A future item-knowledge feature may combine a searchable, versioned knowledge source with read-only save and load-order state. Optional Legacy of the Dragonborn (LOTD) support may add acquisition guidance, source quests or locations, supported-mod context, and collected or displayed state when those values can be determined reliably.

The knowledge source belongs outside the live bridge stream; the bridge should expose only relevant game state. This is separate from the map, bridge validation, and first companion workflow, even if a later client links an item result to a map location.

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
