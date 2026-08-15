# Roadmap

The roadmap is intentionally foundation-first and feature-based. It locks structural semantics that
would be expensive to retrofit across identity, synchronization, transport, or multiple clients,
then validates them through a thin connected product slice before broad feature development. Each
numbered phase should deliver one coherent player-facing capability or one necessary product
foundation through a focused feature branch and pull request.

A roadmap phase describes the intended outcome, behavior, boundaries, dependencies, and acceptance
criteria. Phase status records product order; implementation authority is defined in `AGENTS.md`.

The order records current product dependencies. A later phase may be refined as earlier work reveals
constraints, but it should not be pulled forward silently or bundled into an earlier feature.
`ARCHITECTURE.md` owns cross-cutting architecture decisions, `protocol/schema/README.md` owns the
current wire contract, and `ai/context/protocol/security.md` owns transport and security constraints;
this roadmap records when approved outcomes are delivered rather than duplicating those contracts.

Completed phase numbers are historical and remain stable. Future phases use whole numbers.

## 0. Documentation baseline

**Status:** Complete

### Outcome

DovahLink has a self-contained source of truth for product scope, architecture, protocol ownership, security constraints, development conventions, and maintainer authority before implementation begins.

### Scope and behavior

- Establish the product direction and initial architecture boundaries.
- Define the canonical protocol boundary and initial security constraints.
- Define the contribution, branch, review, and maintainer-approval workflow.
- Copy the AI development conventions needed for Flutter, SKSE, protocol, and integration work into this repository.
- Keep the repository implementation-free until the maintainer explicitly starts the first implementation phase.

### Dependencies and boundaries

This phase has no implementation dependency. It establishes direction and authority but does not approve or pre-create Flutter, bridge, networking, protocol, or feature code.

### Acceptance criteria

The required documentation exists in the repository, agrees on ownership and safety boundaries, and does not depend on instructions from another local project. Later phases still require direct maintainer authorization.

## 0.5 Client and Protocol Foundation

**Status:** Complete

### Outcome

DovahLink has the smallest replaceable Flutter and protocol-facing foundation needed to begin
bridge integration without mixing client structure with transport implementation.

### Scope and behavior

- Establish the Flutter client shell and manual dependency-injection boundary.
- Generate and validate the first protocol-facing client models.
- Define connection domain entities, repository contracts, and use cases.
- Define Redux connection state, actions, reducers, selectors, and a read-only status screen.
- Establish client conventions, fixtures, generated-code rules, and test coverage for this
  foundation.

### Dependencies and boundaries

This foundation does not implement Skyrim integration, a transport, pairing, reconnection, or an
external validation client. It prepares those boundaries without claiming that a connection works.

### Acceptance criteria

The Flutter project analyzes cleanly, its foundation tests pass, protocol models map the approved
fixtures, and the client can render explicit disconnected and connection-error states without
real transport access.

## 1. Skyrim Bridge Foundation

**Status:** Complete

### Outcome

Skyrim can expose a minimal, trustworthy state value to one external validation client through a DovahLink-owned connection boundary.

### Scope and behavior

- Confirm the first supported Skyrim edition and development environment.
- Evaluate SkyrimWebSocket as the reference and reusable foundation instead of rebuilding an equivalent native bridge from scratch.
- Record which parts can be adopted and which must be adapted or excluded to meet DovahLink's runtime, lifecycle, protocol, security, and repository boundaries.
- Keep SkyrimWebSocket-specific types and wire behavior behind DovahLink-owned game-state, application, protocol-mapping, and transport boundaries.
- Use the canonical DovahLink protocol over the approved loopback-only connection.
- Connect one external validation client, show connection state, and display one trustworthy value.
- Document setup, supported versions, failure behavior, and known limitations.

### Dependencies and boundaries

This phase depends on the documentation baseline and the completed Phase 0.5 protocol-facing
foundation. Its external validation client is a separate proof client that speaks the canonical
protocol; this phase does not turn the Phase 0.5 Flutter shell into the connected product client or
create map resources, a UI theme system, remote-device networking, LOTD data, or unrelated bridge
capabilities.

### Acceptance criteria

The reuse decisions are documented, the bridge follows the approved lifecycle and protocol
boundaries, one external validation client can authenticate and display the chosen value without
presenting stale data as current, a connection attempt that fails before the one-time token is
consumed can retry and still succeed, and setup is reproducible. Reconnecting a client that has
already completed one successful session without restarting the bridge is a known Phase 1
limitation addressed by Phase 3 and is not part of this phase's acceptance bar.

## 2. Bridge Identity and Authoritative State Foundation

**Status:** Complete

### Outcome

The implemented protocol and both sides of the connection adopt the identity, state-ownership, and
revision semantics defined in `ARCHITECTURE.md` while the protocol surface is still small.

### Scope and behavior

- Add the approved `bridgeInstanceId`, `playContextId`, `clientId`, and socket-bound `sessionId`
  lifetimes to the canonical contract and adapters.
- Define authoritative state identity as one `bridgeInstanceId`, `playContextId`, and state area;
  a bridge restart creates a new state identity even when the same play context remains loaded.
- Keep `sessionId` scoped to authenticated socket delivery only; reconnecting creates a new session
  without resetting the current authoritative revision.
- Make state revisions belong to that authoritative bridge instance, state area, and play context
  rather than one socket session.
- Advance a revision only when authoritative state changes; unchanged snapshot requests reuse it.
- Invalidate prior state when a new play context replaces the previous loaded game.
- Add an executable bridge-restart acceptance test proving cached state from the previous bridge
  lifetime is rejected, including when its play context and revision match the new bridge's values.
- Keep the independent validation client as proof that the protocol has no Flutter-only behavior.
- Do not silently reinterpret messages from the previously published experimental release as
  already carrying this ownership; that release is archived, not a supported compatibility target.
- Update the schema, fixtures, bridge, validation client, Flutter consumer, and tests together as
  one canonical contract change, not as a separate protocol generation kept alongside an older one.
- Identify compatibility by the DovahLink Bridge/mod release version rather than an independent
  protocol-generation number, per `ai/context/protocol/compatibility.md`.

### Dependencies and boundaries

This phase implements the target semantics in `ARCHITECTURE.md`. It does not add a separate
game-process identifier, concurrent sockets, LAN exposure, new state areas, or gameplay commands.

### Acceptance criteria

Messages identify the correct bridge, play context, client, and socket session; bridge restarts,
reconnects, and loaded save changes cannot make old state current; reconnects do not reset the
authoritative revision; unchanged snapshots do not manufacture revisions; the bridge-restart
acceptance test rejects prior-lifetime cached state; and official and independent clients pass the
canonical contract tests.

## 3. Local Device Pairing and Reconnection

**Status:** Planned

### Outcome

A client pairs with the local bridge once through a short, user-friendly in-game confirmation flow,
then reconnects automatically after transport loss, a client restart, a Bridge restart, or a Windows
restart, without generating or configuring a long token, without restarting Skyrim, and without
seeing another pairing code unless trust was actually removed.

### Scope and behavior

- Introduce pairing as part of the current canonical contract. The Phase 1 `one_time_local_token`
  bootstrap behavior must not be silently reinterpreted as the pairing flow.
- Require an unpaired client to explicitly request pairing; the bridge must not display a pairing
  code on every launch or on every ordinary connection attempt.
- The bridge, not the client, owns whether pairing is currently available. The protocol represents
  pairing-unavailable/initializing, pairing-available, and pairing-in-progress without embedding
  Skyrim UI concepts into the wire contract; pairing must not claim to be ready if the in-game
  confirmation cannot actually be presented.
- Permit only one active pairing challenge globally. A second request while a challenge is active
  does not generate or display a second code, does not replace the existing code, and does not spam
  Skyrim notifications; it reports that pairing is already in progress and stays bound to the
  existing challenge. Another challenge may begin only after the current one succeeds, expires, or
  is explicitly cancelled.
- When pairing is requested, generate a short-lived, single-use six-digit pairing code and display
  it through a native Skyrim notification. The first user-facing implementation must not require
  SkyUI, another UI mod, a separate Papyrus mod, or a separately installed pairing utility. Code
  validation is atomic and fail closed; expired, already-consumed, invalid, and rate-limited attempts
  fail clearly without invoking game-state behavior, and the final expiry and attempt limits are
  recorded in the approved Phase 3 security/protocol design before implementation.
- Let the user enter the displayed code in the DovahLink client. A correct code bootstraps trust only
  for that pairing operation; it is never itself a reusable session credential.
- Use a recoverable pairing handshake instead of an atomic cross-process transaction: the bridge
  moves through explicit `pending`-credential and `trusted` states, and the client moves through
  explicit `pairing`/`confirming`/`trusted` states. The client durably persists its issued credential
  and its `confirming` recovery state before sending final confirmation, and final confirmation is
  idempotent: a client recovering from `confirming` that receives an `already_trusted` outcome for
  its own valid credential treats that as success, not an error. A pending credential the bridge does
  not yet consider trusted must not authenticate an ordinary session. An incomplete pending pairing
  does not need to survive a bridge restart; when the client retries confirmation against a bridge
  that no longer recognizes the pending credential, it discards the incomplete local credential and
  returns to unpaired.
- After successful confirmation, bind a strong, device-scoped credential to the approved `clientId`
  and issue it to the pairing client; that credential is the only credential used for that client's
  later reconnects, and each reconnect still creates a fresh authenticated `sessionId`.
- Persist completed trust so it survives Skyrim, Bridge, and Windows restarts, save changes,
  `playContextId` changes, and `bridgeInstanceId` changes. Persistent trust belongs to the current
  Windows user profile running the client and the Bridge, not to the modpack, the Skyrim
  installation, a particular Bridge process, `bridgeInstanceId`, `playContextId`, or `sessionId`, so
  two Windows users sharing one PC and modpack have independent trusted clients and exporting or
  copying a modpack never copies another user's paired-device trust. Store both the Bridge's trusted-
  client records and the client's own credential through an approved per-user secure-storage
  mechanism for the platform; do not invent cryptography. `bridgeInstanceId` remains ephemeral exactly
  as defined by `ARCHITECTURE.md`; persistent trust does not change that a bridge restart still
  creates a new `bridgeInstanceId`, and the client authenticates again after a restart to receive a
  new `sessionId`.
- Scope `clientId` to the client installation and the Windows user profile running it, so the
  official client does not share one `clientId` and credential between different Windows user
  profiles merely because the executable is installed system-wide.
- Store the device credential through an approved secure-storage mechanism in the client. Tokens,
  pairing codes, credentials, and raw protocol payloads must not appear in normal logs, errors,
  fixtures, or user-facing messages.
- Issue each trusted client a five-digit `shortId` alongside its real `clientId`, generated by the
  bridge and unique among currently trusted clients in that Windows-user trust domain, for
  human-readable administration only; a `shortId` is never authentication or authorization material
  and is never treated as protocol/client identity, and may be reused once it is no longer associated
  with a currently trusted client. Carry an optional `displayName` as presentation-only metadata: not
  unique, never authentication or authorization material, never a substitute for `clientId`, bounded
  in length, and free of control characters or unsafe presentation/logging behavior. A conforming
  third-party or diagnostic client may leave `displayName` absent; requiring the official client to
  collect a user-chosen display name belongs to its own connected-client UX phase.
- Put persistent trust behind a dedicated trust-store abstraction (load, persist, revoke, reset,
  query) rather than pairing logic owning a particular file format directly, and put administration
  behavior (list trusted clients, revoke one, reset all) in a reusable Bridge application/domain
  service rather than inside a Skyrim console-command handler, so console commands, a future Flutter
  management UI, and developer tooling all call the same behavior. An explicit local reset-all-trust
  operation exists from this phase onward. Trust-store corruption or inaccessible persistence fails
  closed: it never crashes Skyrim, never silently trusts a client, never invents or merges uncertain
  credentials, and always supports a clean reset-and-re-pair path.
- Make revocation immediate: revoking a trusted client removes its active trust, invalidates any
  current authenticated session it owns, closes that connection, and rejects reuse of the revoked
  credential; resetting all trust applies the same behavior to every trusted client. A revoked client
  that reconnects with its old credential receives a specific revoked/not-trusted outcome rather than
  a generic transport failure, so it can return to pairing directly. Distinguishing a revoked
  `clientId` from one that was never paired may use a minimal revocation tombstone containing no
  credential; re-pairing an intentionally revoked `clientId` may remove or replace that tombstone and
  establish a new credential.
- Make the existing long-token capability explicit developer authentication rather than normal-user
  authentication: it is enabled only when an explicit `DOVAHLINK_DEV_TOKEN` (or equivalent approved
  configuration) is set, with identical behavior across debug, beta, and release builds. Developer-
  token authentication is a separate provider from device pairing; it must not silently enroll the
  authenticating client into the persistent trusted-device store, and a developer-authenticated
  session still obeys loopback, input-limit, protocol-validation, fresh-`sessionId`, and
  single-connected-client rules.
- Give WebSocket-level Ping/Pong and a bounded idle timeout sole ownership of connection liveness
  once a session is established, instead of an invented DovahLink application heartbeat or guessing a
  TCP socket's state. Every transport/session failure (normal close, client crash, Bridge shutdown,
  transport error, unresponsive peer, idle timeout) converges through one deterministic teardown:
  invalidate `sessionId`, cancel or finish outstanding I/O, close the transport, then release the
  connection slot; a dead `sessionId` can never become valid again. Audit the existing transport
  timeout implementation so the established WebSocket liveness policy does not compete with an
  incompatible lower-level TCP-stream timeout owner; lower-level connection/handshake deadlines may
  still apply before WebSocket ownership begins.
- Reconnect with a valid persisted credential always creates a fresh `sessionId` without requiring
  another six-digit code, without resetting the authoritative revision, without reinterpreting
  `bridgeInstanceId`, and without reviving pending messages from a dead session. If a client crashes
  and restarts quickly enough that its previous connection is still tearing down, treat the retry as
  bounded short retry/backoff against a temporarily busy slot rather than introducing same-client
  connection takeover or generation-replacement semantics; runtime tests must prove that normal
  close, crash, rapid restart, timeout, and Bridge restart all recover automatically.
- Allow distinct clients to pair over time while retaining the single-connected-client limit.
  Concurrent sessions and independent per-client delivery remain Phase 8 work.

### Dependencies and boundaries

This phase depends on Phase 2 and remains loopback-only. The pairing code is a local bootstrap
mechanism, not a substitute for authenticated encryption or a complete LAN trust protocol. Phone
and LAN pairing, discovery, encrypted transport, replay protection, and remote revocation belong to
Phase 21 and must receive a separate security design.

The pairing notification is a runtime-facing adapter concern; it must not place Skyrim presentation
details into the canonical protocol. The protocol should carry pairing state and outcomes, while
the bridge/plugin boundary owns the native in-game notification mechanism. An optional QR-code
convenience flow may be considered for Phase 21, but it must not become a dependency for entering a
code manually or require a separate client or mod.

Persistent trust storage is scoped to the current Windows user, but loopback TCP itself is not proof
of Windows-user identity; the six-digit pairing proof, persistent credential authentication, rate
limits, and loopback restriction remain the controls that apply, and strict isolation from another
simultaneously logged-in local account is a separate threat boundary to solve deliberately if it
becomes required, not something assumed from `127.0.0.1`. See `ai/context/protocol/security.md` for
the full threat-boundary documentation this phase must produce.

The Phase 3 trust-store implementation only needs to satisfy the current single-Bridge-process
requirement, but its boundary must not make later multi-process support require rewriting the
pairing protocol or trust-domain model; Phase 9 will eventually permit multiple Bridge processes for
the same Windows user, and shared per-user trust must be able to gain multi-process synchronization
later without changing client authentication semantics. This phase does not implement Phase 8
concurrent-client delivery, Phase 9 multi-Bridge discovery, or Phase 10 automatic connection/transport
selection; reconnect here targets the already-known local endpoint rather than discovering or
selecting among Bridge processes.

### Acceptance criteria

- An unpaired client can explicitly request pairing and receives a visible six-digit code in Skyrim
  only for that request, and only when the bridge reports pairing as actually available; a second
  request while a challenge is active does not produce a second code.
- A user can complete pairing by entering the code in the DovahLink client without installing a UI
  mod or copying a manually generated long token.
- A valid code is accepted at most once and only within its allowed lifetime; invalid, expired,
  reused, and rate-limited attempts fail without creating a client credential.
- Pairing uses recoverable `pending`/`confirming`/`trusted` semantics: the client does not send final
  confirmation before its credential and recovery state are durably stored, final confirmation is
  idempotent, and recovering into an `already_trusted` outcome after a lost success response is a
  successful outcome, not an error. Incomplete pending pairing may safely disappear on Bridge restart
  without creating trust.
- Successful pairing binds one strong device credential to one `clientId`; persistent trust survives
  Skyrim, Bridge, and Windows restarts, save changes, and `playContextId`/`bridgeInstanceId` changes,
  and is scoped to the Windows user profile rather than the modpack, Skyrim installation, or
  `bridgeInstanceId`, so copying or exporting a modpack cannot copy trusted-device secrets.
  `bridgeInstanceId` still changes on every Bridge runtime, and every authenticated socket still
  receives a fresh `sessionId` that is invalid after disconnect/reconnect.
- The official client's user profile does not accidentally share `clientId`/credential state between
  different Windows users. Trusted clients carry a real `clientId` plus a separate five-digit
  administration-only `shortId` that is never used for authentication, authorization, or identity;
  `displayName` is optional, presentation-only protocol metadata with explicit safe bounds, never
  required from third-party clients.
- Persistent trust is accessed through a dedicated trust-store abstraction, and administration
  (list/revoke/reset) is reusable application/core behavior rather than logic embedded in a Skyrim
  console command. An explicit local reset-all-trust operation exists. Trust-store corruption or
  inaccessibility fails closed without crashing Skyrim and without silently trusting a client, and
  supports a clean reset/re-pair path.
- Revoking or resetting trust immediately invalidates affected active sessions; a revoked credential
  cannot reconnect and instead receives a clear revoked/not-trusted outcome suitable for returning to
  pairing; re-pairing may safely establish a new credential afterward.
- Developer-token authentication exists only when a dev token is explicitly configured, behaves
  identically across debug, beta, and release builds, does not silently enroll that client as a
  persistent trusted device, and still receives a normal fresh `sessionId` under normal transport and
  protocol rules.
- Connection liveness uses coherent WebSocket-level Ping/Pong/idle-timeout behavior instead of socket-
  state guessing or an invented application heartbeat, and session teardown deterministically
  invalidates the session and releases the single-client slot after close, error, timeout, or
  shutdown.
- A paired client reconnects into a fresh authenticated session with fresh state after transport
  loss, a client restart, or a Bridge restart, using its existing credential and without displaying
  another pairing code unless trust was actually removed; force-closing/crashing the client and an
  immediate restart both recover automatically through bounded retry, not connection takeover.
- The loopback-only restriction and single-connected-client limit remain intact; this phase does not
  accidentally enable LAN or concurrent-client behavior, and does not implement Phase 9/10 Bridge
  discovery or endpoint-selection behavior.
- Independent and official clients use the same canonical pairing contract, with no Flutter-only or
  Skyrim-specific wire behavior.
- Pairing secrets, credentials, and developer tokens are absent from normal logs, errors, fixtures,
  and user-facing diagnostics.

## 4. Live State Synchronization Foundation

**Status:** Planned

### Outcome

The bridge pushes changing state from shared authoritative stores without requiring polling or
allowing delivery pressure to block Skyrim.

### Scope and behavior

- Replace the Phase 1 request/response loop with full-duplex asynchronous delivery.
- Prefer native events and sample only where no trustworthy event exists.
- Treat rate classes as maximum frequencies and publish unsolicited replaceable state only on
  authoritative change.
- Separate replaceable state, ordered reliable events, and recovery/control traffic.
- Coalesce replaceable state to its latest value under pressure.
- Always deliver initial, recovery, and explicitly requested snapshots, even when the state is
  unchanged; these snapshots reuse the current authoritative revision.
- Keep game-thread capture small and perform network I/O and serialization elsewhere.
- Instrument capture, queues, coalescing, disconnects, and recovery before tuning thresholds.

### Dependencies and boundaries

This phase depends on Phases 2 and 3, uses only the Phase 1 `character` state area, and keeps the
one-connected-client limit. Heavy resources remain outside the live stream.

Reliable-event delivery is scoped to one authenticated session. Reconnection establishes fresh
state snapshots and does not replay the previous session's queued events. Durable cross-session
replay requires a separately approved acknowledgement and persistence contract.

### Acceptance criteria

A subscriber receives an initial snapshot followed by complete post-change state rather than a patch;
initial, recovery, and explicitly requested snapshots are delivered even when unchanged, while
unchanged unsolicited replaceable state produces no traffic; replaceable state coalesces, reliable
events stay ordered, and a client that cannot consume them in time is explicitly disconnected
without stalling Skyrim.

## 5. PC / Second-Screen Baseline

**Status:** Planned

### Outcome

The first native Flutter product client connects on the same PC and makes connection, recovery, and
sample state understandable without developer documentation.

### Scope and behavior

- Extend the Phase 0.5 shell into a connected desktop-sized client.
- Use the same canonical protocol and pairing flow available to independent clients.
- Present connecting, connected, recovering, incompatible, unavailable, stale, and disconnected
  states clearly.
- Keep the client useful when Skyrim is absent or optional state is unavailable.
- Add only the client structure needed by this thin connected slice.

### Dependencies and boundaries

This phase validates Phases 2 through 4. It remains loopback-only and excludes automatic discovery,
broad player state, mobile packaging, dashboard customization, and actions.

### Acceptance criteria

The desktop client connects, shows trustworthy sample state, reconnects after interruption, rejects
stale context, and explains actionable failures without developer guidance.

## 6. Core UI Theme System

**Status:** Planned

### Outcome

DovahLink establishes reusable Skyrim-inspired presentation before feature screens multiply.

### Scope and behavior

- Define shared Flutter tokens and components for typography, color, panels, icons, spacing, shape,
  elevation, motion, and responsive sizing.
- Cover loading, empty, unavailable, stale, recovering, and error states.
- Support contrast, text scaling, accessibility, and reduced motion.
- Apply the system to the PC baseline before broad features.
- Keep future adapters declarative and presentation-only.

### Dependencies and boundaries

The native theme works without inspecting Skyrim or requiring a UI mod. Detection, adapters, custom
theme data, and dashboard behavior remain later phases.

### Acceptance criteria

Existing surfaces use shared tokens or components and remain useful at supported sizes and
accessibility settings without optional resources.

## 7. Live Player State

**Status:** Planned

### Outcome

The connected, themed client proves a useful single-client flow with focused character information.

### Scope and behavior

- Select approved values such as identity, level, health, magicka, stamina, location, or combat.
- Expose only values sourced reliably from the supported runtime.
- Add a versioned slice with snapshots and live updates.
- Mark unavailable, delayed, recovering, and stale values honestly.
- Reuse shared synchronization and UI foundations.

### Dependencies and boundaries

This read-only phase validates the complete single-client path. It excludes inventory, equipment,
map navigation, and commands.

### Acceptance criteria

Approved values remain accurate through play, reconnect, and play-context replacement; unavailable
state degrades clearly; and cross-side tests prove bridge/client agreement.

## 8. Multi-Client Runtime Foundation

**Status:** Planned

### Outcome

One bridge serves concurrent clients while capture remains shared and one slow client cannot
destabilize Skyrim or healthy clients.

### Scope and behavior

- Replace the one-client slot with a bounded concurrent-client registry.
- Give each client independent session, capabilities, subscriptions, queues, recovery, and diagnostics.
- Fan out shared authoritative state without repeated Skyrim reads.
- Keep revisions shared while delivery state remains client-specific.
- Disconnect or resynchronize slow consumers without blocking others.
- Keep navigation and layout local to each client.
- Give the official Flutter client no privileged treatment.

### Dependencies and boundaries

This phase follows the Phase 7 single-client proof and remains loopback-only. It excludes LAN,
synchronized layouts, accounts, collaboration, and control permissions.

### Acceptance criteria

At least two clients receive consistent state and recover independently; client count does not
multiply equivalent Skyrim reads; and a slow client does not stall a healthy client.

## 9. Multi-Bridge and Local Discovery Foundation

**Status:** Planned

### Outcome

Multiple bridge processes coexist on one machine without port collisions or ambiguous selection.

### Scope and behavior

- Treat the Phase 1 port as a preference rather than identity.
- Select another local port when the preferred port is occupied.
- Publish a small non-secret same-machine discovery record for each bridge.
- Validate records against authenticated `bridgeInstanceId` and tolerate stale records.
- Remove records on clean shutdown and isolate records owned by other instances.
- Let official and independent clients enumerate the same discovery surface.
- Avoid a resident machine-level discovery service.

### Dependencies and boundaries

This phase depends on Phases 2 and 8. LAN discovery belongs to Phase 21.

### Acceptance criteria

Two harness bridges run concurrently without manual port editing, appear as distinct choices, accept
independent clients, and cannot corrupt each other's discovery state.

## 10. Automatic Connection and Transport Selection

**Status:** Planned

### Outcome

The client chooses the best path to the intended local bridge while preserving manual control.

### Scope and behavior

- Keep connection candidates separate from bridge identity.
- Remember the preferred bridge independently from its endpoint.
- Prefer approved same-machine candidates and retain manual fallback.
- Observe coarse Skyrim process presence without reading game memory.
- Represent stopped, launching, initializing, ready, loading, playing, recovery, and failures.
- Retry transient failures with bounded backoff and stop on terminal conditions.
- Let later LAN candidates join the same policy without a second connection architecture.

### Dependencies and boundaries

This phase consumes Phase 9 and remains loopback-only. It excludes a resident monitor, LAN access,
internet exposure, hosted relay, and accounts.

### Acceptance criteria

The client reconnects to the intended bridge, never confuses simultaneous instances, backs off
responsibly, and provides actionable manual recovery.

## 11. Mod Awareness

**Status:** Planned

### Outcome

DovahLink exposes the minimum trustworthy load-order and capability information later features need.

### Scope and behavior

- Identify only information required by approved features.
- Expose stable capabilities instead of client-side mod-name checks.
- Treat missing, unsupported, or ambiguous information as unavailable.
- Version compatibility knowledge independently where practical.
- Document privacy, performance, and failure behavior.

### Dependencies and boundaries

This phase excludes UI theme detection, LOTD behavior, a plugin platform, and broad compatibility
promises.

### Acceptance criteria

Clean and modded setups produce trustworthy capability summaries, unsupported cases fall back safely,
and features do not consume raw CommonLib details.

## 12. Interactive Map Foundation

**Status:** Planned

### Outcome

A player follows live position and trustworthy marker state on a responsive companion map.

### Scope and behavior

- Establish viewport, pan, zoom, coordinate conversion, and responsive presentation.
- Track player position and direction as lightweight overlays.
- Present marker state only where reliable.
- Preserve unknown, discovered, cleared, and unavailable distinctions.
- Support the base worldspace first.
- Keep static resources separate from live overlays.

### Dependencies and boundaries

This phase depends on live location and the core theme. It excludes routes, arbitrary destinations,
every worldspace, and a parallel navmesh database.

### Acceptance criteria

The base map tracks accurately, handles worldspace and play-context changes, survives reconnects,
and never invents discovery or cleared state.

## 13. Map Asset and Worldspace System

**Status:** Planned

### Outcome

Map imagery and worldspaces grow without bloating live traffic or loading every map.

### Scope and behavior

- Define versioned client packages for maps, tiles, marker artwork, and slow-changing assets.
- Cache, validate, update, and invalidate packages deliberately.
- Lazy-load DLC and supported mod worldspaces.
- Investigate installed map derivation only where technically and legally appropriate.
- Define attribution, licensing, fetching, fallback, and incompatibility behavior.
- Keep live state separate from packages.

### Dependencies and boundaries

This phase extends Phase 12. Assets do not become live protocol messages.

### Acceptance criteria

Packages load independently from live state, additional worldspaces do not inflate ordinary traffic,
and missing assets cannot break synchronization.

## 14. Quests

**Status:** Planned

### Outcome

The player reviews active quests and objectives without opening the journal.

### Scope and behavior

- Present approved fields, ordering, completion, failure, and active state.
- Handle hidden, localized, modded, malformed, or rapidly changing data.
- Link objectives to markers only when trustworthy.
- Prefer event-driven reconciliation where reliable.

### Dependencies and boundaries

This read-only phase does not activate quests, change stages, repair quests, or infer hidden locations.

### Acceptance criteria

Representative quests remain accurate through loads and reconnects, and hidden information is not
fabricated.

## 15. Navigation / Path Guidance

**Status:** Planned

### Outcome

The map shows route guidance only when Skyrim provides a trustworthy route.

### Scope and behavior

- Validate exposure of native Clairvoyance or Guide routes as lightweight polylines.
- Render and refresh Skyrim-calculated segments.
- Explain unavailable, partial, invalidated, or recalculating routes.
- Invalidate on play-context, worldspace, objective, or source changes.
- Measure runtime cost before selecting refresh behavior.

### Dependencies and boundaries

DovahLink does not own pathfinding, maintain a navmesh database, or present approximations as
authoritative. Arbitrary map destinations remain deferred.

### Acceptance criteria

A reliable native route renders and invalidates correctly; an unreliable route is explicitly
deferred.

## 16. Inventory

**Status:** Planned

### Outcome

The player browses and searches trustworthy read-only inventory.

### Scope and behavior

- Represent stable identity, count, category, weight, value, and approved metadata.
- Handle stacks, instances, renamed, enchanted, and mod-added items.
- Reconcile looting, trading, crafting, loading, and reconnect changes.
- Keep filtering client-side where practical.
- Keep enrichment separate from ownership state.

### Dependencies and boundaries

This phase does not equip, drop, consume, transfer, favorite, or modify items.

### Acceptance criteria

Inventory reconciles without stale duplicates, remains responsive at realistic sizes, and does not
repeatedly rescan during idle play without reason.

## 17. Equipment

**Status:** Planned

### Outcome

The client shows current equipment and its relationship to inventory.

### Scope and behavior

- Represent approved weapons, armor, ammunition, hands, voice, and supported slots.
- Handle dual wielding, two-handed items, shields, transformations, and reliable modded slots.
- Link equipment to stable inventory identity.
- Reconcile Skyrim and mod-driven changes promptly.

### Dependencies and boundaries

This read-only phase excludes equip, unequip, loadouts, and remote item actions.

### Acceptance criteria

Equipment matches authoritative state and cannot remain falsely equipped after invalidation.

## 18. Magic, Spells, Shouts, and Powers

**Status:** Planned

### Outcome

The player browses learned abilities and current selection or usability state.

### Scope and behavior

- Separate spells, shouts, powers, and approved ability types.
- Present supported category, cost, cooldown, effect, selection, favorite, and hotkey metadata.
- Handle mod-added abilities and incomplete metadata.
- Keep reference enrichment separate from live state.

### Dependencies and boundaries

This read-only phase excludes casting, equipping, unlocking, and spending resources.

### Acceptance criteria

Abilities appear in correct categories, selection and cooldown remain current, and unknown behavior
degrades without fabricated values.

## 19. Favorites and Hotkeys

**Status:** Planned

### Outcome

The player reviews favorites and hotkey assignments.

### Scope and behavior

- Show supported favorited items and abilities.
- Represent native assignments and conflicts where reliable.
- Link entries to inventory, equipment, and magic identities.
- Reconcile game and mod-driven changes.

### Dependencies and boundaries

This read-only phase excludes activation, item use, casting, and assignment changes.

### Acceptance criteria

Favorites remain consistent through changes, reconnects, and play-context replacement; unresolved
entries remain visible without misidentification.

## 20. Customizable Dashboard

**Status:** Planned

### Outcome

Players arrange delivered modules for their second-screen workflow.

### Scope and behavior

- Provide movable and resizable modules for delivered features.
- Enforce desktop grid, minimum-size, overflow, and responsive constraints.
- Save, restore, migrate, reset, and recover local preferences.
- Keep dashboard and navigation state local to each client.
- Keep the default dashboard useful without configuration.

### Dependencies and boundaries

This phase depends on the core theme and real modules. It excludes cloud layouts, arbitrary widgets,
and protocol-level dashboard configuration.

### Acceptance criteria

Valid layouts persist and recover, modules cannot be resized into broken states, and corrupt
preferences fall back safely.

## 21. Secure LAN Transport and Network Discovery

**Status:** Planned

### Outcome

Approved LAN clients securely discover and connect to the intended bridge without trusting the LAN.

### Scope and behavior

- Complete the threat model and pairing design required by `ai/context/protocol/security.md`.
- Use established authenticated encryption; do not invent cryptography.
- Discover multiple hosts and bridges without treating address as identity.
- Authenticate endpoints before trusting advertised metadata.
- Preserve per-client authorization, revocation, replay protection, and session binding.
- Add approved wired and Wi-Fi/LAN candidates where platforms permit.
- Feed candidates into Phase 10 and preserve manual connection.

### Dependencies and boundaries

This phase depends on identity, multi-client isolation, local discovery, and automatic selection. It
does not imply internet exposure, hosted relay, accounts, or cloud presence.

### Acceptance criteria

Clients distinguish and securely connect to the intended bridge; spoofed or unpaired endpoints are
not trusted; revocation works; and localhost remains preferred where applicable.

## 22. Mobile / Tablet Client

**Status:** Planned

### Outcome

The companion experience works naturally on supported phones and tablets through secure LAN.

### Scope and behavior

- Provide touch-optimized portrait navigation and module layouts.
- Support landscape second-screen presentation where appropriate.
- Reuse domain and protocol boundaries with device-specific presentation.
- Preserve pairing, recovery, background, resume, and network transitions.
- Use the Phase 10 policy with manual fallback.
- Keep layout preferences local and provide mobile defaults.

### Dependencies and boundaries

This phase depends on Phase 21 and does not imply internet access, hosted relay, accounts, or
identical layouts.

### Acceptance criteria

A device pairs and reconnects securely, survives background and network changes, presents existing
features accessibly, and cannot confuse another discovered bridge.

## 23. Item Knowledge and Search

**Status:** Planned

### Outcome

Players search versioned reference information without confusing it with live save truth.

### Scope and behavior

- Keep large reference data outside the live bridge stream.
- Provide approved names, categories, descriptions, guidance, and source context.
- Link entries to live state only through stable identities and explicit confidence.
- Make provenance, version, localization, caching, and updates visible.

### Dependencies and boundaries

Reference data cannot claim current ownership or location without authoritative state. LOTD remains
separate.

### Acceptance criteria

Search remains responsive, provenance is clear, and outdated knowledge cannot create false live-state
claims.

## 24. Legacy of the Dragonborn Integration

**Status:** Planned

### Outcome

Supported LOTD setups relate item knowledge and live save state to museum progress.

### Scope and behavior

- Detect explicitly supported LOTD versions and required data.
- Add collection/display state and acquisition context only where reliable.
- Keep static museum knowledge distinct from live state.
- Explain unsupported versions, missing patches, replicas, and ambiguity.

### Dependencies and boundaries

LOTD is optional and cannot be required by inventory, knowledge, or map features. It does not modify
museum state.

### Acceptance criteria

Supported setups report verified state, unsupported setups fall back to ordinary knowledge, and
ambiguous values are not authoritative.

## 25. Installed UI Detection

**Status:** Planned

### Outcome

DovahLink identifies selected UI or font resources without weakening its native theme.

### Scope and behavior

- Detect only explicitly supported installations and versions.
- Expose presentation capabilities rather than filesystem paths.
- Handle conflicts, overrides, incomplete installs, and unsupported versions.
- Cache only with clear invalidation.
- Let the player return to the native theme.

### Dependencies and boundaries

Detection follows the core theme and dashboard and does not modify Skyrim files or require one mod
manager.

### Acceptance criteria

Supported installs are identified reproducibly, ambiguous setups do not activate adapters, and
failure cannot prevent startup.

## 26. Optional UI Mod Adapters

**Status:** Planned

### Outcome

Players may complement supported Skyrim UI setups without coupling behavior to those mods.

### Scope and behavior

- Add opt-in adapters for approved versions.
- Map approved presentation values into the theme boundary.
- Define ownership and fallback for every value.
- Evaluate a constrained versioned `dovahlink-theme.json` before implementation approval.
- Reject executable behavior and protocol changes from theme data.

### Dependencies and boundaries

Adapters cannot replace dashboard structure, behavior, protocol models, or executable code.

### Acceptance criteria

Adapters pass visual, fallback, accessibility, and version checks; invalid resources fall back to
the native theme.

## 27. Safe Companion Authorization Foundation

**Status:** Planned after read-only product validation

### Outcome

DovahLink can safely add individually approved actions later without exposing a generic command API
or granting control through read access.

### Scope and behavior

- Define per-client permissions independently from authentication and sessions.
- Keep read and control capabilities separate.
- Define command identity, requesting `clientId` and `sessionId`, result, failure, timeout, and
  idempotency.
- Reject replayed, stale-session, stale-play-context, unauthorized, and unsupported commands before
  game code.
- Define authorization, revocation, audit-safe diagnostics, and capability negotiation.
- Define deterministic multi-client conflict handling.
- Validate the machinery without adding gameplay mutation.

### Dependencies and boundaries

This phase follows read-only validation and depends on identity, multi-client isolation, and security.
It adds no equipment, favorites, hotkey, map-marker, fast-travel, console, Papyrus, or other action.
Each action needs its own product decision, security review, validation plan, roadmap phase, and
implementation approval.

### Acceptance criteria

The contract can deny control independently from reads; unauthorized, replayed, stale, conflicting,
and unknown test commands are rejected deterministically; decisions are attributable; and no Skyrim
mutation is exposed.

## 28. Runtime Profiling and Advanced Bridge Hardening

**Status:** Planned

### Outcome

Measured usage drives final bridge tuning without speculative complexity.

### Scope and behavior

- Profile game-thread capture, sampling, serialization, memory, queues, fan-out, and recovery.
- Compare one-client and multi-client cost and verify reads remain shared.
- Profile heavy feature scenarios.
- Tune capacities, rates, executors, and caches only from evidence.
- Consolidate native readers only after features prove a shared abstraction.
- Expand runtime support with reproducible tests.
- Version protocol-affecting improvements.

### Dependencies and boundaries

This phase is evidence-driven and does not authorize mutation, scripting, remote exposure, or
hypothetical protocol complexity.

### Acceptance criteria

Every optimization names a measured problem, demonstrates improvement, preserves recovery, and keeps
game-thread cost negligible for approved targets.

## 29. CommonLib Dependency Maintenance Audit

**Status:** Planned

### Outcome

The pinned `commonlibsse-ng-flatrim` dependency remains reproducible and deliberately maintained
before the next public release.

### Scope and behavior

- Keep the Phase 1 dependency pinned for the current supported runtime.
- Audit CommonLibSSE-NG and the selected registry for relevant fixes and releases.
- Update pins only after bridge, toolchain, protocol, integration, and in-game checks.
- Record reviewed versions, decision, results, and limitations before the next public release.
- Define a reviewed fallback if the package route cannot provide a validated build.

### Dependencies and boundaries

This audit does not expand runtimes, replace the CommonLib boundary, or automate updates.

### Acceptance criteria

The exact dependency set builds reproducibly, upstream and registry status are reviewed, the selected
pin passes validation, and the decision is documented before the next public release.

## Deferred possibilities

These ideas require a demonstrated need and separate product, architecture, security, and validation
decisions before entering the ordered roadmap:

- Individual companion actions, including equipment, favorites or hotkeys, map markers, and fast travel
- Arbitrary console or Papyrus execution through the bridge
- Arbitrary map-selected destinations
- A general plugin or extension platform
- Internet or hosted remote connectivity
- Accounts, cloud storage, or cross-device cloud synchronization
- Broad compatibility promises for unsupported mods, UI themes, or Skyrim editions
