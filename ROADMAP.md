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
  returns to unpaired. The client-side half of this requirement -- durable `clientId`/credential
  persistence, `confirming` recovery state, and relaunch recovery -- is implemented ahead of
  schedule in the pulled-forward `sdk/dart/dovahlink_client/` package described by Phase 5 below;
  see `ai/context/sdk/persistence.md`. This does not close Phase 3, whose remaining scope (the
  six-digit code, Skyrim notification, trust store, revocation, and administration surface) is
  Bridge-side work this pull-forward does not touch.
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
  Concurrent sessions and independent per-client delivery remain Phase 9 work.

### Dependencies and boundaries

This phase depends on Phase 2 and remains loopback-only. The pairing code is a local bootstrap
mechanism, not a substitute for authenticated encryption or a complete LAN trust protocol. Phone
and LAN pairing, discovery, encrypted transport, replay protection, and remote revocation belong to
Phase 22 and must receive a separate security design.

The pairing notification is a runtime-facing adapter concern; it must not place Skyrim presentation
details into the canonical protocol. The protocol should carry pairing state and outcomes, while
the bridge/plugin boundary owns the native in-game notification mechanism. An optional QR-code
convenience flow may be considered for Phase 22, but it must not become a dependency for entering a
code manually or require a separate client or mod.

Persistent trust storage is scoped to the current Windows user, but loopback TCP itself is not proof
of Windows-user identity; the six-digit pairing proof, persistent credential authentication, rate
limits, and loopback restriction remain the controls that apply, and strict isolation from another
simultaneously logged-in local account is a separate threat boundary to solve deliberately if it
becomes required, not something assumed from `127.0.0.1`. See `ai/context/protocol/security.md` for
the full threat-boundary documentation this phase must produce.

The Phase 3 trust-store implementation only needs to satisfy the current single-Bridge-process
requirement, but its boundary must not make later multi-process support require rewriting the
pairing protocol or trust-domain model; Phase 10 will eventually permit multiple Bridge processes for
the same Windows user, and shared per-user trust must be able to gain multi-process synchronization
later without changing client authentication semantics. This phase does not implement Phase 9
concurrent-client delivery, Phase 10 multi-Bridge discovery, or Phase 11 automatic connection/transport
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
  accidentally enable LAN or concurrent-client behavior, and does not implement Phase 10/11 Bridge
  discovery or endpoint-selection behavior.
- Independent and official clients use the same canonical pairing contract, with no Flutter-only or
  Skyrim-specific wire behavior.
- Pairing secrets, credentials, and developer tokens are absent from normal logs, errors, fixtures,
  and user-facing diagnostics.

## 3.1 Live Pairing Challenge UX

**Status:** Planned

### Outcome

A person pairing a device can see how much time their code has left, can bring the code back to
the screen on demand or automatically after a mistake, and can cancel a pairing attempt in
progress -- all without changing what a completed pairing means or how durable trust is stored.
This phase completes and hardens the live, ephemeral pairing-challenge experience Phase 3
established; it does not touch the durable trust model, which remains Phase 3.2's job.

### Scope and behavior

- The pairing challenge keeps its existing overall expiry; this phase changes how that expiry and
  the code are surfaced and protected, not how long a challenge lasts.
- The Bridge remains the sole authority over challenge expiration. It exposes remaining duration
  (conceptually `expiresInSeconds`) rather than an absolute wall-clock timestamp, so the SDK/app
  renders a local countdown and refreshes authoritative pairing state as expiry approaches. Flutter
  must not own expiry semantics itself.
- The active challenge is owned by the requesting `clientId`, not by a WebSocket/session. Only one
  device may be actively pairing at a time; a second device attempting to pair while a challenge is
  active is rejected with an explicit, generic outcome distinct from `pairing_status`'s existing
  `in_progress` (which today only ever means the *requesting* client's own challenge is still
  active) -- conceptually `other_device_pairing` -- that reveals nothing about the owning device or
  its code. The owning device may reconnect and resume its existing challenge.
- A disconnect while a challenge is owned (normal close, network loss, or connection timeout)
  preserves the challenge for a 10-second reconnect grace period: the same `clientId` reconnecting
  within that window regains its existing code, expiry, and remaining-attempt count without an
  automatic Skyrim re-notification. If the grace period elapses first, the challenge is cancelled
  outright, its code destroyed, and the slot freed for another device. A challenge never transfers
  to a different device. This grace period does not apply when the challenge ended because of
  explicit cancellation, a protocol/security-driven termination, or an administrative trust action
  that deliberately ends it -- revocation, or a block/trust-reset/factory-reset once Phase 3.2 adds
  them -- since those are deliberate endings, not connectivity hiccups.
- "Show code again" is a dedicated operation (conceptually `pairing_renotify`), not a repurposing of
  ordinary `pairing_request` -- a repeated `pairing_request` still must not spam Skyrim
  notifications, exactly as today. Only the owning `clientId` may invoke it, and it re-displays the
  same active code; the code itself never reaches the app. It carries its own 5-second Bridge-owned
  cooldown, represented in the UI as a loading/cooldown state rather than an error. A request during
  the cooldown is neither queued nor silently ignored: it returns an explicit cooldown/rate-limited
  result carrying enough remaining-duration information for the UI to render correctly. The Bridge
  does not add invasive Skyrim UI hooks to detect exactly when `RE::DebugNotification` visually
  decays; the fixed cooldown is the source of truth.
- An incorrect confirmation automatically re-displays the code in Skyrim (unless the hard attempt
  limit below has just been reached), using wording that distinguishes a wrong attempt from the
  original display. This automatic re-notification has its own independent rate limit and must not
  affect or reset the manual "show again" cooldown -- the two are intentionally separate paths, and
  neither may spam notifications.
- Confirmation attempts are protected two ways: a pacing limit permits at most one evaluated code
  validation per second (an earlier attempt gets an explicit rate-limited/cooldown response,
  consumes no failed attempt, and the UI stays in a loading state until validation is allowed
  again), and a hard limit permits at most 5 evaluated wrong codes per challenge. The fifth wrong
  code cancels the challenge immediately, destroys the code, requires an entirely new pairing
  request, and displays a distinct too-many-attempts notification; the now-invalid code is never
  shown again.
- An explicit cancellation operation (conceptually `pairing_cancel`) may be invoked by the owning
  `clientId` or an appropriate Skyrim/admin command, and works whether the challenge is still active
  or already holding a credential pending acknowledgement. Cancelling a pending credential destroys
  it, returns pairing to idle, and requires the client to discard any uncommitted local credential
  state. Cancellation is idempotent without pretending work occurred: it reports `cancelled` when it
  actually ended something, and a distinct `already_idle` when there was nothing to cancel. It never
  revokes an already-trusted device, creates a revocation record, or alters any unrelated persisted
  trust.
- A credential issued after a correct code no longer stays pending indefinitely: the
  pending-credential state expires after 5 minutes. The 10-second disconnect grace period no longer
  applies once a challenge reaches this state -- existing recovery semantics may still let the
  owning client reconnect within the 5 minutes to complete acknowledgement. Expiry destroys the
  pending credential, returns pairing to idle, and requires starting a new pairing process.

### Dependencies and boundaries

This phase depends on Phase 3's completed pairing state machine, trust store, and session
infrastructure, and extends the existing pairing wire messages without changing what a completed
pairing means or how trust is persisted -- durable trust identity and administration belong to
Phase 3.2, not here. It does not add a new Skyrim UI or mod dependency; the in-game side remains
native `RE::DebugNotification` text.

### Acceptance criteria

- A person pairing a device can see remaining time on the active code and can bring the code back
  to the screen on demand, without generating a new challenge or resetting/extending its expiry.
- A wrong code leaves the same challenge, code, and expiry in place (short of the hard attempt
  limit) and automatically redisplays the code in Skyrim with wording distinguishing the mistake.
- Only one device pairs at a time; a competing device gets a generic busy result with no
  information about the owner or the active code, and the owning device can reconnect and resume.
- A short disconnect (normal close, network loss, timeout) does not cost the owning device its
  challenge, code, expiry, or attempt count within the 10-second grace period, and does not trigger
  an automatic re-notification on that reconnect; a longer disconnect cleanly cancels the challenge
  and frees the slot for another device.
- "Show code again" and automatic wrong-code re-notification are independently rate-limited and
  neither affects the other's cooldown; a request during a cooldown returns an explicit
  rate-limited result with remaining-duration information, never a silent no-op or a queued retry.
- At most one code validation is evaluated per second, and at most 5 wrong evaluated codes are
  allowed per challenge; the sixth attempt cannot occur because the fifth already cancelled the
  challenge, destroyed the code, and required a fresh pairing request.
- Cancellation works both while a challenge is active and while a credential is pending
  acknowledgement, reports `cancelled` or `already_idle` truthfully, and never revokes trust,
  creates a revocation record, or touches unrelated persisted trust.
- Once a credential is pending, the 10-second disconnect grace period no longer applies; the owning
  client may still reconnect within the pending credential's own 5-minute window to complete
  acknowledgement, and a pending credential that is never acknowledged within that window expires,
  returning pairing to idle and requiring a fresh pairing process.

## 3.2 Known Device & Trust Administration

**Status:** Planned

### Outcome

The durable trust model gains an explicit, unified device identity and a full administrative
lifecycle around it -- rename, revoke, block, unblock, forget, reset, and factory reset -- so an
already-paired device can be managed deliberately instead of only ever being trusted or gone. This
is a real security/trust-model change, kept deliberately separate from Phase 3.1's live-challenge
UX work.

### Scope and behavior

- A unified, durable Known Device model replaces the current conceptual split of independent
  trusted records, revocation tombstones, and (new) block records. A device becomes a persistent
  Known Device only on its first *successful* pairing; merely requesting or attempting pairing
  creates no persistent record.
- Each Known Device carries durable identity metadata: `clientId`, a stable `shortId`, an optional
  `displayName`, and `createdAt`. There is no `lastSeenAt`; `createdAt` is the authoritative field
  for device age and ordering. A Known Device is always in exactly one of four durable states --
  `Trusted`, `Revoked`, `Blocked`, or `Unpaired` -- and only `Trusted` carries a usable credential.
  `Blocked` additionally persists `blockedAt`, administrative metadata only; blocks never expire on
  their own.
- A Known Device's `shortId` is allocated once, at creation, and stays stable across every
  transition it can go through (`Trusted -> Revoked -> Trusted`; `Trusted -> Blocked -> Unblocked ->
  Unpaired -> Trusted`), including while `Blocked`. This is a deliberate change from the trust
  model's current behavior, where a `shortId` becomes reusable as soon as its client is no longer
  trusted (`ai/context/protocol/security.md`'s "Persistent local trust"); under the Known Device
  model a `shortId` is reserved for as long as the Known Device record exists, and becomes reusable
  only once that device is explicitly forgotten or removed by a factory reset.
- `displayName` stays presentation-only metadata, never security identity, and duplicate names
  across different devices are allowed. A `Trusted` device may rename itself while authenticated; an
  empty rename clears the name. Re-pairing without supplying a new name preserves the existing one;
  supplying a valid new name replaces it. When a display name is shared by more than one listed
  device, the admin listing disambiguates it only in presentation -- suffixed `#1`, `#2`, `#3`...,
  numbered oldest-to-newest by `createdAt`, recomputed whenever the list is rendered (so forgetting
  the oldest duplicate shifts the remaining suffixes). This suffix is never persisted, never part of
  identity, and never accepted in place of `shortId`, which remains the one stable administrative
  identifier.
- Revocation keeps its existing meaning: it removes current trust and invalidates the credential but
  permits future pairing. It immediately disconnects every active session belonging to the device,
  invalidates its credential, transitions it to `Revoked`, and preserves its `clientId`, `shortId`,
  `displayName`, and `createdAt`. Re-pairing a `Revoked` Known Device restores `Trusted` while
  keeping the same Known Device identity.
- Blocking is strictly stronger than revocation: it prevents a known device identity from
  authenticating *or* entering pairing again until explicitly unblocked. Only `Trusted` and
  `Revoked` Known Devices can be blocked in this phase -- an arbitrary, never-authorized `clientId`
  cannot be blocked, since blocking targets an existing Known Device record, not a bare identity
  string. Blocking atomically destroys any trusted credential, cancels any pairing the identity owns
  (active or pending), immediately disconnects every active session for that device, transitions it
  to `Blocked`, and records `blockedAt`. A blocked device is rejected as early as `hello`: it
  receives neither a normal authenticated session nor an unpaired/restricted pairing session, and
  is never issued a new Skyrim pairing code. The rejection uses an explicit, canonical `blocked`
  outcome, distinct from `revoked` -- the two must not be conflated -- and discloses no stored
  `shortId`, `displayName`, or other administrative metadata to the unauthenticated connection.
  Blocked connection attempts update no persistent metadata and produce no Skyrim notification.
- "Device," for this phase's blocking scope, means the persistent DovahLink client installation
  identity carried by `clientId`. A reinstall or local reset that mints a new `clientId` can present
  as an unrelated new device; this phase adds no hardware fingerprinting, OS-machine fingerprinting,
  other invasive platform identifiers, or physical-device attestation to close that gap. A stronger,
  reinstall-resistant device-identity capability -- potentially including physical-device
  attestation -- is recorded as a deferred possibility below, to be reconsidered alongside the
  later LAN/mobile security phases, and is explicitly not committed scope here.
- Unblocking removes the block, transitions the device to `Unpaired`, and requires a completely
  fresh pairing flow -- it does not restore the old credential, though it preserves `clientId`,
  `shortId`, `displayName`, and `createdAt`.
- Administration exposes both a general Known Device list (carrying each device's state) and a
  filtered blocklist view; a `Blocked` device appears in both. User-facing/admin commands normally
  target `shortId`; internal domain/security APIs continue to use `clientId` as the canonical
  security identity. Block and unblock are safely retryable: repeating either operation returns a
  meaningful, truthful outcome for the device's current state rather than silently no-opping or
  falsely claiming a transition occurred.
- An explicit "forget" operation (conceptually `forget <shortId>`) is allowed only for Known Devices
  in `Revoked` or `Unpaired` -- a `Trusted` device must be revoked first, and a `Blocked` device
  must be explicitly unblocked first; forgetting never implicitly lifts a block. Forgetting deletes
  the Known Device record entirely (its `clientId`/`shortId` association, display name, `createdAt`,
  and revocation history) and frees its `shortId` for future allocation. Any credential or client
  identity presented afterward is unknown/unauthenticated; if that same installation later pairs
  again, it is treated as a completely new Known Device with a new `shortId` and `createdAt`.
- A recoverable Reset Trust operation, distinct from Factory Reset, immediately disconnects every
  trusted session, cancels active/pending pairing, invalidates every trusted credential, and
  transitions every formerly `Trusted` Known Device to `Revoked` -- while preserving every Known
  Device record, its `shortId`, `displayName`, `createdAt`, and every existing block. It executes
  immediately and does not require Factory Reset's stronger confirmation flow.
- Factory Reset is the destructive first-run reset: it removes trusted credentials, every Known
  Device record, revocations, the blocklist and its timestamps, display names, creation timestamps,
  and `shortId` reservations, cancels all active/pending pairing, and disconnects every active
  session -- afterward, trust/pairing state is indistinguishable from a first run. Because it is
  destructive, the initial factory-reset request performs no mutation, disconnects no one, and
  alters no trust; Skyrim instead generates and displays a short confirmation code, and the
  destructive reset executes only once that code is supplied back through the appropriate admin
  command. A simple repeated command or a literal "confirm" is not an acceptable confirmation model.
- Developer-token authentication (`ai/context/protocol/security.md`'s "Developer authentication")
  stays a separate, explicit provider; normal Known Device blocking semantics do not redefine a
  developer-token session as a paired device, and developer-token authentication is not folded into
  the Known Device lifecycle merely to reuse block/unblock.
- Administrative operations that invalidate access take effect immediately, with no window where an
  already-authenticated connection keeps operating until its next reconnect. This extends Phase 3's
  existing "Revocation is immediate" guarantee (`security.md`'s "Persistent local trust") to apply
  uniformly across Block, Revoke, Reset Trust, and Factory Reset alike.

### Dependencies and boundaries

This phase depends on Phase 3's trust store, trust administration surface, and session
infrastructure, and on Phase 3.1 only insofar as both extend the same pairing challenge; it does
not depend on Phase 3.1's specific UX behavior. It changes the durable trust model's shape (a
unified Known Device record replacing independent trusted/tombstone/block concepts) and the
`shortId` reuse rule described above, so it must not be implemented as a purely additive change
without accounting for that shift. It does not implement hardware/OS/platform device fingerprinting
or physical-device attestation; that remains a deferred possibility. It does not change developer-
token authentication's separate provider status.

This phase also redefines what "reset" means, and that redefinition must be handled deliberately,
not as a rename in place. Phase 3's already-shipped `TrustAdminService::Reset()` today performs the
full, unconditional wipe (every trusted record and revocation tombstone removed, no confirmation
step) that this document now calls **Factory Reset**. The new, narrower **Reset Trust** operation
-- which preserves Known Device records and only revokes sessions/credentials -- is genuinely new
behavior with no existing implementation. A future implementer must not assume today's `Reset()`
call site can simply be renamed to mean "Reset Trust"; its existing behavior maps to Factory Reset
(which additionally needs the new confirmation-code gate), and Reset Trust needs to be built as a
distinct operation alongside it.

### Acceptance criteria

- A device becomes a persistent Known Device only after a successful pairing, never merely from a
  pairing attempt or request.
- Revoking a Known Device disconnects its active sessions immediately, invalidates its credential,
  and leaves it able to re-pair back to `Trusted` under the same Known Device identity.
- Blocking a `Trusted` or `Revoked` Known Device destroys its credential, cancels any pairing it
  owns, disconnects its active sessions immediately, and thereafter rejects it at `hello` with a
  distinct `blocked` outcome that exposes no stored metadata -- without producing a Skyrim
  notification or updating persisted metadata for the rejected attempt.
- Unblocking returns a device to `Unpaired` without restoring its old credential, requiring a fresh
  pairing flow, while its `clientId`, `shortId`, `displayName`, and `createdAt` all survive
  unchanged.
- An authenticated `Trusted` device can rename itself, an empty rename clears the display name, and
  re-pairing without supplying a new name preserves the existing one; when two or more listed
  devices share a display name, the admin listing disambiguates them in presentation only, numbered
  oldest-to-newest by `createdAt`, without ever persisting the suffix or accepting it in place of
  `shortId`.
- Administration exposes both a general Known Device list carrying each device's state and a
  filtered blocklist view, with every `Blocked` device appearing in both.
- A Known Device's `shortId` never changes and never becomes available for reuse while that Known
  Device record exists, across every state transition it can undergo.
- Repeated block/unblock operations against the same device return truthful, meaningful outcomes
  reflecting its actual current state rather than a silent no-op or a false claim of transition.
- Forgetting a device is possible only from `Revoked` or `Unpaired`, deletes its record completely,
  frees its `shortId`, and never implicitly lifts a block.
- Reset Trust immediately revokes every trusted session and credential while preserving every Known
  Device record (including blocks), and requires no destructive-confirmation flow; Factory Reset
  removes all trust/pairing state entirely, requires a Skyrim-displayed confirmation code before its
  destructive mutation runs, and leaves the system indistinguishable from first run afterward.
- Block, Revoke, Reset Trust, and Factory Reset all disconnect every affected active session
  immediately, with no already-authenticated connection able to keep operating until its next
  reconnect.
- Developer-token sessions are never treated as Known Devices and are unaffected by Known Device
  block/unblock semantics.

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

## 5. Dart Client SDK Foundation

**Status:** Planned. The package scaffold, protocol/transport layer, and persistence boundary
(`clientId`, credential, `CONFIRMING` pairing-recovery state, behind a Windows DPAPI-backed
`ClientStorage`) were pulled forward to unblock Phase 3's client-side pairing recovery, per
`ai/context/sdk/persistence.md`. Bridge-version compatibility detection, reconnect, revisions,
subscriptions, snapshots, and retiring the app's separate `features/connection/` Redux protocol
code remain undone, so this phase is not complete.

### Outcome

Dart applications can participate correctly in DovahLink — transport, Bridge-version compatibility
detection, authentication, pairing recovery, reconnect, session and authoritative-state identity,
revisions, subscriptions, snapshots, recovery, and reusable client persistence — without
implementing that behavior themselves, and the official Flutter application becomes the first
production consumer proving the supported SDK API is sufficient to build a complete client.

### Scope and behavior

- Create the real `sdk/dart/dovahlink_client/` package and establish it as a first-class repository
  ownership boundary alongside `app/`, `bridge/`, `protocol/`, and `integration/`, per
  `ARCHITECTURE.md` and `ai/context/sdk/`.
- Migrate the reusable Dart-side connection, compatibility, authentication, pairing, reconnect,
  session, revision, subscription, and recovery behavior already implemented directly in `app/` by
  Phases 2 through 4 into the SDK boundary, rather than redesigning approved semantics.
- Establish the SDK's explicit supported Bridge/mod-version range and its own persistence boundary
  (stable local `clientId`, client credential, pairing recovery state, reusable cache metadata),
  versioned and migration-owned by the SDK per `ai/context/sdk/persistence.md`.
- Expose one underlying client engine through a small simple API plus focused expert capability
  views (lifecycle, diagnostics, administration), per `ai/context/sdk/architecture.md` and
  `api-design.md`; do not build a second parallel service stack.
- Wire the official Flutter application through the SDK's public API and retire its parallel
  app-private protocol/client implementation in this same phase; the app must not construct raw
  transport, compatibility, authentication, pairing, reconnect, revision, or subscription logic
  after this phase completes.
- Keep the SDK repository-internal and unpublished; publication, package stability guarantees, and
  a public release workflow remain a separate future decision.

### Dependencies and boundaries

This phase depends on Phases 2, 3, and 4 and consumes their approved identity, pairing/reconnection,
and live-synchronization semantics rather than redesigning them. It does not implement Phase 9
concurrent-client delivery, Phase 10 multi-Bridge discovery, Phase 11 automatic connection/transport
selection, or Phase 22 secure LAN transport; when those phases are implemented, their Dart client
behavior extends the SDK rather than being built privately into the app again. The independent .NET
validation client remains a separate implementation of the canonical contract and does not consume,
wrap, or generate from the Dart SDK.

### Acceptance criteria

- The `sdk/dart/dovahlink_client/` package exists with a curated public API; internal transport,
  codec, persistence, compatibility, and state-machine types are not accidentally exported.
- The SDK declares an explicit supported Bridge/mod-version range rather than inferring compatibility
  from SemVer, and canonical contract changes are assessed against that declared range per
  `ai/context/protocol/compatibility.md`.
- The official Flutter application consumes the SDK exclusively for normal DovahLink communication;
  its parallel app-private protocol/client stack is retired in this phase, not left running alongside
  the SDK.
- One underlying client engine backs every exposed API view; there is no duplicate transport,
  session, or cache stack behind a "simple" and an "advanced" surface.
- SDK-owned persistence (client credential, pairing recovery state, cache metadata) is versioned and
  migration-owned by the SDK; the app never needs to understand or migrate that private schema.
- The independent .NET validator still passes the same canonical fixtures without depending on the
  Dart SDK.
- The SDK is not published outside the repository as part of this phase.

## 6. PC / Second-Screen Baseline

**Status:** Planned

### Outcome

The first native Flutter product client connects on the same PC and makes connection, recovery, and
sample state understandable without developer documentation.

### Scope and behavior

- Extend the Phase 0.5 shell into a connected desktop-sized client.
- Connect through the Dart Client SDK's public API rather than app-private protocol/client code,
  proving the SDK sufficient to build a complete connected client.
- Present connecting, connected, recovering, incompatible, unavailable, stale, and disconnected
  states clearly.
- Keep the client useful when Skyrim is absent or optional state is unavailable.
- Add only the client structure needed by this thin connected slice.

### Dependencies and boundaries

This phase validates Phases 2 through 5. It remains loopback-only and excludes automatic discovery,
broad player state, mobile packaging, dashboard customization, and actions.

### Acceptance criteria

The desktop client connects, shows trustworthy sample state, reconnects after interruption, rejects
stale context, and explains actionable failures without developer guidance.

## 7. Core UI Theme System

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

## 8. Live Player State

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

## 9. Multi-Client Runtime Foundation

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

This phase follows the Phase 8 single-client proof and remains loopback-only. It excludes LAN,
synchronized layouts, accounts, collaboration, and control permissions.

### Acceptance criteria

At least two clients receive consistent state and recover independently; client count does not
multiply equivalent Skyrim reads; and a slow client does not stall a healthy client.

## 10. Multi-Bridge and Local Discovery Foundation

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

This phase depends on Phases 2 and 9. LAN discovery belongs to Phase 22.

### Acceptance criteria

Two harness bridges run concurrently without manual port editing, appear as distinct choices, accept
independent clients, and cannot corrupt each other's discovery state.

## 11. Automatic Connection and Transport Selection

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

This phase consumes Phase 10 and remains loopback-only. It excludes a resident monitor, LAN access,
internet exposure, hosted relay, and accounts.

### Acceptance criteria

The client reconnects to the intended bridge, never confuses simultaneous instances, backs off
responsibly, and provides actionable manual recovery.

## 12. Mod Awareness

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

## 13. Interactive Map Foundation

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

## 14. Map Asset and Worldspace System

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

This phase extends Phase 13. Assets do not become live protocol messages.

### Acceptance criteria

Packages load independently from live state, additional worldspaces do not inflate ordinary traffic,
and missing assets cannot break synchronization.

## 15. Quests

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

## 16. Navigation / Path Guidance

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

## 17. Inventory

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

## 18. Equipment

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

## 19. Magic, Spells, Shouts, and Powers

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

## 20. Favorites and Hotkeys

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

## 21. Customizable Dashboard

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

## 22. Secure LAN Transport and Network Discovery

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
- Feed candidates into Phase 11 and preserve manual connection.

### Dependencies and boundaries

This phase depends on identity, multi-client isolation, local discovery, and automatic selection. It
does not imply internet exposure, hosted relay, accounts, or cloud presence.

### Acceptance criteria

Clients distinguish and securely connect to the intended bridge; spoofed or unpaired endpoints are
not trusted; revocation works; and localhost remains preferred where applicable.

## 23. Mobile / Tablet Client

**Status:** Planned

### Outcome

The companion experience works naturally on supported phones and tablets through secure LAN.

### Scope and behavior

- Provide touch-optimized portrait navigation and module layouts.
- Support landscape second-screen presentation where appropriate.
- Reuse domain and protocol boundaries with device-specific presentation.
- Preserve pairing, recovery, background, resume, and network transitions.
- Use the Phase 11 policy with manual fallback.
- Keep layout preferences local and provide mobile defaults.

### Dependencies and boundaries

This phase depends on Phase 22 and does not imply internet access, hosted relay, accounts, or
identical layouts.

### Acceptance criteria

A device pairs and reconnects securely, survives background and network changes, presents existing
features accessibly, and cannot confuse another discovered bridge.

## 24. Item Knowledge and Search

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

## 25. Legacy of the Dragonborn Integration

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

## 26. Installed UI Detection

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

## 27. Optional UI Mod Adapters

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

## 28. Safe Companion Authorization Foundation

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

## 29. Runtime Profiling and Advanced Bridge Hardening

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

## 30. CommonLib Dependency Maintenance Audit

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
