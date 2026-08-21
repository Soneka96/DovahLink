# Stage 3 — Local Device Pairing and Reconnection

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./02-bridge-identity-and-authoritative-state.md) · [Next stage](./04-live-state-synchronization-foundation.md)

## 3. Local Device Pairing and Reconnection

**Status:** Complete

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

**Status:** Complete

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

**Status:** Complete

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
  creates no persistent record. The Bridge owns the authoritative administrative state, and the
  Skyrim/admin surface may expose the full truth: `Trusted`, `Revoked`, `Blocked`, or `Unpaired`.
- Each Known Device carries durable identity metadata: `clientId`, a stable `shortId`, an optional
  `displayName`, and `createdAt`. There is no `lastSeenAt`; `createdAt` is the authoritative field
  for device age and ordering. A Known Device is always in exactly one of four durable states --
  `Trusted`, `Revoked`, `Blocked`, or `Unpaired` -- and only `Trusted` carries a usable credential.
  `Blocked` additionally persists `blockedAt`, administrative metadata only; blocks never expire on
  their own.
- A Known Device's `shortId` is allocated once, at creation, and stays stable across every
  transition it can go through (`Trusted -> Revoked -> Trusted`; `Trusted -> Blocked -> Unblocked ->
  Unpaired -> Trusted`; and `Unpaired -> Blocked`), including while `Blocked`. This is a deliberate change from the trust
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
  authenticating *or* entering pairing again until explicitly unblocked. Any existing non-`Blocked`
  Known Device (`Trusted`, `Revoked`, or `Unpaired`) may be blocked; an arbitrary, never-known
  `clientId` cannot be blocked, since blocking targets an existing Known Device record, not a bare
  identity string. Repeating Block for an already `Blocked` device returns the truthful
  already-blocked outcome. Blocking atomically destroys any trusted credential, cancels any pairing
  the identity owns (active or pending), immediately disconnects every active session for that
  device, transitions it to `Blocked`, and records `blockedAt`. A blocked device is rejected as
  early as `hello`: it
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
- A recoverable Reset Trust operation, distinct from Factory Reset, applies only these state
  transitions: `Trusted -> Revoked`; `Revoked`, `Blocked`, and `Unpaired` remain unchanged. For
  every device transitioning from `Trusted` to `Revoked`, it immediately invalidates/destroys the
  credential and active device-credential session while preserving the Known Device record,
  `clientId`, `shortId`, `displayName`, `createdAt`, and other required identity metadata. It also
  cancels every pending/in-progress pairing globally, leaves developer-token configuration
  untouched, and leaves developer-token sessions unaffected. It executes immediately and does not
  require Factory Reset's stronger confirmation flow.
- Factory Reset is the destructive first-run reset: it removes trusted credentials, every Known
  Device record, revocations, the blocklist and its timestamps, display names, creation timestamps,
  and `shortId` reservations, cancels all active/pending pairing, and disconnects every active
  session -- afterward, trust/pairing state is indistinguishable from a first run. Its confirmation
  flow is independent from normal pairing: the initial local admin request performs no mutation,
  Skyrim generates and displays a single-use six-digit numeric code, and the code is confirmed only
  through the Skyrim/admin surface. The code expires after 60 seconds; one wrong code invalidates
  the challenge immediately; an expired or wrong confirmation performs zero destructive changes;
  remote clients cannot confirm it; and the normal `PairingSession`/pairing slot is not reused.
  A simple repeated command or a literal "confirm" is not an acceptable confirmation model.
- Developer-token authentication (`ai/context/protocol/security.md`'s "Developer authentication")
  stays a separate, explicit provider; normal Known Device blocking semantics do not redefine a
  developer-token session as a paired device, and developer-token authentication is not folded into
  the Known Device lifecycle merely to reuse block/unblock. Reset Trust leaves developer-token
  configuration and sessions unaffected; Factory Reset terminates developer-token sessions but
  leaves the configured developer token available for later authentication.
- Administrative operations that invalidate access take effect immediately, with no window where an
  already-authenticated connection keeps operating until its next reconnect. This extends Phase 3's
  existing "Revocation is immediate" guarantee (`security.md`'s "Persistent local trust") to apply
  uniformly across Block, Revoke, Reset Trust, and Factory Reset alike.
- All administrative invalidation uses one canonical authenticated-session event, conceptually
  `session_invalidated { reason: revoked | blocked | trust_reset | factory_reset }`, rather than
  separate operation-specific messages or a misleading `credential_invalidated` event. The logical
  order is: apply the authoritative state change; invalidate/destroy affected credentials where
  applicable; make future authentication and pairing obey the new state; best-effort send and flush
  `session_invalidated(reason)`; then force-close the affected session. Delivery is never a security
  dependency: no acknowledgement is required, and a hostile, broken, or stalled client cannot delay
  its removal. Offline clients that later present stale credentials receive explicit `blocked` or
  `revoked` handshake rejection as appropriate; a credential left over after Factory Reset has no
  matching record to classify this way and instead receives the ordinary
  unrecognized-credential/unpaired outcome, per `ai/context/protocol/security.md`'s "Administrative
  session invalidation".

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
- Blocking a `Trusted`, `Revoked`, or `Unpaired` Known Device destroys any credential, cancels any
  pairing it owns, disconnects its active sessions immediately, and thereafter rejects it at `hello`
  with a distinct `blocked` outcome that exposes no stored metadata -- without producing a Skyrim
  notification or updating persisted metadata for the rejected attempt. Unknown/random `clientId`
  values cannot create Blocked records, and a concurrent authentication cannot establish a trusted
  session after the authoritative Block transition completes.
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
- Reset Trust converts only `Trusted -> Revoked`, destroys those credentials, invalidates their
  active device sessions, cancels all pending pairing, preserves every Known Device identity field
  and existing block, leaves `Revoked`/`Blocked`/`Unpaired` unchanged, and leaves developer-token
  configuration and sessions unaffected.
- Block, Revoke, Reset Trust, and Factory Reset all disconnect every affected active session
  immediately, with no already-authenticated connection able to keep operating until its next
  reconnect. Each affected session receives best-effort `session_invalidated` with the typed reason
  `blocked`, `revoked`, `trust_reset`, or `factory_reset` before force-close; security does not
  depend on delivery or acknowledgement.
- Offline stale credentials receive explicit `blocked` or `revoked` handshake outcomes, except after
  Factory Reset, where the deleted record leaves nothing to classify against and the credential
  instead receives the ordinary unrecognized-credential/unpaired outcome. Factory
  Reset requires the independent local six-digit, 60-second, single-use confirmation flow, with one
  wrong code invalidating the challenge and no destructive change on wrong or expired confirmation;
  it disconnects every active session, including developer-token sessions, but leaves the configured
  developer token available for later authentication.
- Developer-token sessions are never treated as Known Devices and are unaffected by Known Device
  block/unblock or Revoke semantics; Reset Trust leaves them connected, while Factory Reset
  terminates them and preserves their configuration.
## 3.3 Client Trust-State Integration

**Status:** Planned

### Outcome

The current Dart SDK and existing Flutter connection experience correctly react, in real time, to
authoritative trust and session changes introduced by Phase 3.2. The SDK exposes accurate typed
reasons for developers and debugging, while the official Flutter UI intentionally presents all
administrative invalidation reasons as the same generic unavailable/disconnected experience with a
Retry action. This phase integrates the completed Bridge-side trust authority from Phase 3.2; it
does not become the full future Known Device administration SDK/UI or API.

### Scope and behavior

- Observe the long-lived connection continuously. An established session ending must propagate
  promptly through SDK state; the SDK must not discover a Bridge-side disconnect only when a later
  request/response operation happens.
- Distinguish ordinary transport loss -- normal game exit, crash, Bridge crash, local transport or
  network disappearance -- from an authenticated `session_invalidated(reason)` event. Transport loss
  preserves the existing device credential and `clientId`, enters the appropriate disconnected or
  unavailable state, and may use normal bounded reconnect/backoff. Administrative invalidation is
  terminal for the current session and exposes its authoritative reason.
- Expose typed SDK-level reasons/states for `revoked`, `blocked`, `trustReset`, and `factoryReset`.
  These are public semantic models/events/states consistent with the SDK architecture, not raw
  protocol-string comparisons spread through consumers. Third-party SDK consumers may inspect or
  display the precise reason; the official Flutter UI maps all four to the same generic
  `Unable to connect to DovahLink`-style presentation and Retry action.
- Preserve the local `clientId` across revoke, unblock, reset, relaunch, and recovery. Do not persist
  `blocked`, `revoked`, `trustReset`, or `factoryReset` as durable authoritative trust state across
  application restarts; the Bridge remains authoritative. Persist only the existing SDK-owned local
  identity, credential, and recovery information.
- For a device-credential session, delete the local credential on `revoked`, `blocked`,
  `trustReset`, or `factoryReset` invalidation/rejection as applicable, preserve `clientId`, expose
  the typed reason, do not automatically start pairing, and wait for explicit/manual Retry. A
  `factoryReset` invalidation on a developer-token session ends that session but does not delete the
  configured developer token.
- After an authoritative administrative invalidation (`revoked`, `blocked`, `trustReset`, or
  `factoryReset`), the SDK must not automatically reconnect or automatically re-enter pairing for
  the purpose of recovering trust -- neither immediately nor through a slow or periodic background
  retry. There is no automatic path back to a trusted session; recovery waits for an explicit,
  user-initiated manual Retry. This is distinct from ordinary transport failure: because the
  credential remains valid and no administrative invalidation was received, normal transport loss
  (Skyrim closing, Skyrim crashing, Bridge crash, local transport loss, network loss) still follows
  the ordinary bounded reconnect/backoff policy described above. After manual Retry, a previously
  Revoked device may enter normal pairing, a still-Blocked device receives the typed blocked
  rejection without a pairing code, and a now-Unpaired device may pair again using the same
  `clientId`; no new identity is generated merely because it was previously Blocked or Revoked.
- Keep the SDK responsible for reusable client authentication persistence, typed trust/recovery
  semantics, and session handling; keep Flutter responsible for connection UI wording and
  interaction. Do not add a competing app-private trust authority, administration UI, or transport
  stack. This is not the full Known Device administration SDK/UI phase.
- Add SDK, client, and integration coverage for continuous invalidation observation, transport-loss
  versus administrative-reason distinction, typed reasons, credential cleanup, clientId
  preservation, manual Retry, no uncontrolled reconnect/pair loops, and identical official UI
  presentation.

### Dependencies and boundaries

This phase depends on the authoritative Bridge behavior in Phase 3.2 and uses the pulled-forward
client persistence and SDK foundation already described under Phase 5. It validates the current
SDK/client reaction to trust changes; it does not implement Bridge-side Known Device storage,
listing, rename, block/revoke/reset operations, a complete administration UI/API, LAN trust, or
hardware/device attestation. Phase 3.3 also pulls forward the single inbound SDK receiver/router
that the continuous-observation requirement above depends on — matching replies to pending
operations by correlationId instead of treating the next incoming message as the reply — and
classifies its own pending operations (timeout handling, reconnect revalidation,
administrative-invalidation interruption) using the retry-safety/session-requirement/timeout-class
model. Both the receiver/router contract and the operation-metadata model are owned by
`ai/context/sdk/architecture.md` and `ai/context/sdk/api-design.md` respectively; this phase
implements and validates them for trust-state integration only and does not restate either
contract here.

### Acceptance criteria

- The SDK learns active administrative invalidation immediately from the canonical event, without
  waiting for another normal request, and exposes typed `revoked`, `blocked`, `trustReset`, or
  `factoryReset` state/reason information without parsing protocol strings.
- Unexpected socket loss does not delete credentials; it preserves the existing credential and
  `clientId` and follows ordinary disconnected/unavailable reconnect policy.
- Administrative reasons are not persisted as authoritative local state across app restarts, and
  relevant invalidation or stale-credential rejection removes obsolete Known Device credentials
  while preserving `clientId`.
- No automatic re-pair occurs after Revoke or Block, no uncontrolled reconnect loop exists, and
  manual Retry is available. Revoke can recover into pairing after Retry; Blocked still denies
  pairing; Unblocked can pair again with the same `clientId`.
- The official Flutter connection UI intentionally presents `revoked`, `blocked`, `trustReset`, and
  `factoryReset` identically as generic DovahLink unavailable/disconnected UI with Retry, while
  third-party SDK consumers remain free to inspect or display the precise typed reason.
- Revoke, unblock, Reset Trust, and Factory Reset produce the correct client state without
  conflating blocked, revoked, disconnected, or temporarily unavailable conditions; developer-token
  Factory Reset sessions end without deleting developer-token configuration.
- SDK, client, and integration tests prove continuous observation, transport-loss versus
  administrative invalidation, typed state, credential handling, `clientId` preservation,
  manual-Retry recovery, no automatic re-pair/loop behavior, and the identical official UI policy.
- The Flutter client reacts through SDK state and does not implement a parallel trust store,
  administration surface, authentication flow, or raw-error interpretation.
