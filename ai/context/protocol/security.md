# Protocol and transport security

Security rules apply before the bridge accepts any client connection. A local-network connection is not trusted merely because it is local.

## Phase 1 exposure

- The first connection proof binds to loopback only (`127.0.0.1` and `::1`). It must not listen on a LAN or wildcard address.
- The bridge must reject connections whose remote address is not loopback during this phase.
- The bridge requires a cryptographically random, one-time local connection token before accepting `hello`. The token expires after 5 minutes or first successful use, whichever comes first.
- Token validation and consumption are atomic: exactly one connection can successfully consume a token.
- Failed token attempts are globally limited to 5 per 60 seconds; further attempts are rejected until the window expires.
- The token is supplied out of band by the maintainer during development and is never committed, persisted in source, or sent to a non-loopback peer.
- LAN, Wi-Fi, or remote-device support is blocked until the maintainer approves a pairing and authentication design.
- A configuration value or command-line flag must not silently bypass the loopback restriction.

## Persistent local trust

- Normal users authenticate through pairing, not a configured long token. A normal user never
  generates a token, edits a Vortex/environment variable, or copies an authentication secret to
  connect; that remains available only as explicit developer authentication (see "Developer
  authentication" below).
- Pairing availability is owned by the Bridge, not guessed by the client from arbitrary delays. The
  protocol represents pairing-unavailable/initializing, pairing-available, and pairing-in-progress;
  the Bridge must not claim pairing is available when the in-game confirmation cannot actually be
  presented.
- Only one pairing challenge may be active at a time, globally. A request while a challenge is active
  does not generate a second code, replace the existing code, or produce a second Skyrim
  notification; it reports that pairing is already in progress and stays bound to the existing
  challenge. Another challenge may begin only after the current one succeeds, expires, or is
  explicitly cancelled.
- Pairing-code validation is short-lived, single-use, atomic, attempt-limited, and fail closed,
  matching "Phase 1 exposure"'s token-validation guarantees above; the final expiry and attempt-limit
  values are recorded here once the maintainer approves them.
- Pairing uses a recoverable confirmation handshake rather than an atomic cross-process transaction:

  ```text
  Bridge: NONE -> CHALLENGE_ACTIVE (valid code) -> PENDING_CREDENTIAL (confirmation) -> TRUSTED
  Client: UNPAIRED -> PAIRING (credential received) -> persist credential + recovery state
          -> CONFIRMING (accepted / already trusted) -> TRUSTED
  ```

  A pending credential must not authenticate an ordinary trusted session. The client durably saves
  its issued credential and its `CONFIRMING` recovery state before sending final confirmation. Final
  confirmation is idempotent; a client recovering from `CONFIRMING` that receives `already_trusted`
  for its own valid credential treats that as a successful outcome, not an error. A client that fails
  before saving the credential creates no durable trust and may pair again once the Bridge's pending
  challenge expires. A client that saves the credential but crashes before confirming retries
  confirmation on restart. If the Bridge restarted while the credential was only pending, it reports
  the pending credential as no longer known/valid; the client discards its incomplete local
  credential and returns to unpaired. If a pending record survives while Revoke, Block, Reset Trust,
  or Factory Reset changes its mutation fence, the bridge returns the distinct `pairing_invalidated`
  pairing outcome so the client can distinguish administrative invalidation from a missing pending
  record while taking the same safe discard-and-restart action. The normal coordinated
  administration path cancels pending state before the ACK is processed, so that path truthfully
  returns `pending_not_found` instead.
- This state machine maps to canonical messages, all Connection-category per
  `ai/context/protocol/conventions.md`:

  **Phase 3 (core pairing):** `pairing_request` (client, on an `unpaired`-tier session per
  "Hello authentication and session trust tiers" below — starts or queries a challenge, no payload),
  `pairing_status` (bridge reply to `pairing_request`: `unavailable`/`available`/`in_progress`, never
  the code itself), `pairing_confirm` (client, carries the user-entered code plus an optional
  `displayName` — `CHALLENGE_ACTIVE -> PENDING_CREDENTIAL`: on a valid code the bridge generates the
  credential, holds it in memory only, and returns it), `pairing_ack` (client, echoes back the
  credential it just durably saved -- this is the wire form of "final confirmation";
  `PENDING_CREDENTIAL -> TRUSTED` via `TrustStore::Persist`, only once this arrives), and
  `pairing_outcome` (bridge reply to `pairing_confirm` and `pairing_ack`, distinguished by its
  `outcome` field: `credential_issued` carries the pending credential; `trusted` carries the
  committed credential's `shortId`; `already_trusted` is `pairing_ack`'s idempotent-retry success
  case; `expired`/`invalid`/`pacing_limited`/`hard_limit_reached` carry no credential; `pending_not_found`
  is what a `pairing_ack` retry gets after a Bridge restart lost the in-memory pending credential;
  `pairing_invalidated` is what an administrative mutation returns when a matching pending
  credential survives long enough to be rejected by its stale mutation fence before persistence).

  **Phase 3.1 (pairing UX):** Two additional client-originated messages, both on `unpaired`-tier
  sessions: `pairing_renotify` (client requests redisplay of the active code, no payload, bridge
  replies with `pairing_outcome` bearing `renotified`/`renotify_cooldown`/`already_idle`), and
  `pairing_cancel` (client gives up ownership of an active challenge or pending credential, no
  payload, bridge replies with `pairing_outcome` bearing `cancelled`/`already_idle`). Phase 3.1 also
  replaced the single undifferentiated `rate_limited` outcome with `pacing_limited` (attempt too
  soon, doesn't count wrong) and `hard_limit_reached` (5th wrong attempt, cancels challenge), per
  `roadmap/03-local-device-pairing-and-reconnection.md`'s "3.1 Live Pairing Challenge UX". `pairing_status` now carries `expiresInSeconds`,
  a number only while `clientId`'s own code is actively counting down (`available`, or `in_progress`
  while still `CHALLENGE_ACTIVE`) and `null` for `in_progress` while only a `PENDING_CREDENTIAL` is
  owned -- see `protocol/schema/README.md`'s `pairing_status` section for the exact
  number/null/omitted contract -- and adds `other_device_pairing` to report that a different
  `clientId` owns the active challenge without revealing anything else about it.

  The in-memory pending-credential record is keyed to the single active connection (this phase's
  single-connected-client limit makes a second concurrent claimant structurally impossible) and
  never persists past a Bridge restart, matching "Incomplete pending pairing does not need to
  survive a bridge restart."
- Persist completed trust outside the Skyrim/modpack files, scoped to the Windows user profile
  running the client and the Bridge — not to the modpack, the Skyrim installation, a particular
  Bridge process, `bridgeInstanceId`, `playContextId`, or `sessionId` — through an approved per-user
  secure-storage mechanism for the platform. Do not invent cryptography. Loopback TCP itself does not
  establish this scoping; see "Local-OS-user threat boundary" below.
- The Bridge's approved per-user secure-storage mechanism for the current (Windows) platform is
  DPAPI (`CryptProtectData`/`CryptUnprotectData`) in its default per-user scope —
  `CRYPTPROTECT_LOCAL_MACHINE` is never set — so the OS itself ties the encrypted material to the
  logged-in Windows user, matching the user-profile scoping above; DPAPI is Windows' standard
  per-user secret-protection primitive, not invented cryptography. The trust snapshot is serialized
  as JSON, reusing the project's existing `boost::json` dependency rather than a second
  serialization format, before encryption, and stored as one file under the current user's local
  application-data directory. Any DPAPI failure, missing/malformed file structure, or JSON-shape
  mismatch on load is corruption per the trust-store's fail-closed contract, never a silent partial
  trust; a missing file is a valid empty store, not corruption. Saves write to a temporary file and
  atomically replace the previous one, so a crash mid-write cannot leave a partially written trust
  file. This decision binds only the Windows persistence adapter; the `TrustStore` domain object
  stays platform-independent, and a future non-Windows platform implements the same
  `ITrustStorePersistence` port against its own per-user secure-storage primitive (for example
  Linux Secret Service/keyring, or macOS Keychain) without this document's trust semantics
  changing.
- Scope `clientId` to the client installation and the Windows user profile running it; the official
  client must not share one `clientId`/credential between different Windows user profiles merely
  because the executable is installed system-wide. No Windows username, SID, hostname, or other
  OS-user identifier needs to become part of the wire protocol to establish this scoping.
- Each trusted client receives a five-digit `shortId`, generated by the Bridge, unique among
  currently trusted clients in that Windows-user trust domain, for human-readable administration
  only. A `shortId` is never authentication or authorization material and is never treated as
  protocol/client identity; knowing or guessing one grants no authentication capability. An optional
  `displayName` is presentation-only protocol metadata: not unique, never authentication or
  authorization material, never a substitute for `clientId`, bounded in length, and free of control
  characters or unsafe presentation/logging behavior. A conforming third-party or diagnostic client
  may leave it absent.
- Access persistent trust through a dedicated trust-store boundary (load, persist, revoke, reset,
  query) rather than pairing logic owning a particular file format directly. Trust administration
  (list trusted clients, revoke one, reset all) lives in a reusable Bridge application/domain
  service, not inside a Skyrim console-command handler, so console commands, a future Flutter
  management UI, and developer tooling all call the same behavior. An explicit local reset-all-trust
  operation exists. Trust-store corruption or inaccessible persistence fails closed: it never crashes
  Skyrim, never silently trusts a client, never invents or merges uncertain credentials, and always
  supports a clean reset-and-re-pair path. This phase's trust-store implementation only needs to
  satisfy a single Bridge process, but its boundary must not make later multi-process synchronization
  (Phase 10, multi-Bridge) require rewriting the pairing protocol or trust-domain model.
- Revocation is immediate: revoking a trusted client removes its active trust, invalidates its
  current authenticated session, closes that connection, and rejects reuse of the revoked credential;
  resetting all trust applies the same behavior to every trusted client. A revoked client that
  reconnects with its old credential receives a specific revoked/not-trusted outcome rather than a
  generic transport failure. Distinguishing a revoked `clientId` from one that was never paired may
  use a minimal revocation tombstone containing no credential; re-pairing an intentionally revoked
  `clientId` may remove or replace that tombstone and establish a new credential.

## Administrative session invalidation

Phase 3.2 extends persistent local trust with Bridge-owned Known Device states: `Trusted`, `Revoked`,
`Blocked`, and `Unpaired`. Block applies to a `Trusted` or `Revoked` Known Device -- never an
`Unpaired` one, which stays not eligible, and never creates a record for an unknown `clientId`; a
repeated block reports the already-blocked state. Blocked stale credentials are rejected explicitly
as `blocked`, while revoked stale credentials are rejected explicitly as `revoked`. These
administrative truths are available to the authenticated/admin surfaces and SDKs; the official
Flutter UI may intentionally present them identically.

When an administrator deliberately invalidates an authenticated session, the Bridge uses one
canonical unsolicited terminal event, `session_invalidated`, with a typed reason of `revoked`,
`blocked`, `trust_reset`, or `factory_reset`. It is not named `credential_invalidated`, because
Factory Reset terminates developer-token sessions while leaving the configured developer token
valid. The security ordering is authoritative state change, credential invalidation where applicable,
future authentication/pairing enforcement, best-effort send/flush of the event, then forced close.
No client acknowledgement is required and event delivery is never a security dependency. A client
that receives a duplicate or late `session_invalidated` for a session it has already torn down
treats it as a no-op: the event is idempotent and never re-triggers credential cleanup or a second
state transition.

Reset Trust converts only `Trusted` to `Revoked`, destroys those device credentials, invalidates
their sessions with `trust_reset`, cancels all pending pairing, preserves Known Device identity and
metadata, and leaves developer-token configuration and sessions unaffected. Factory Reset uses an
independent Skyrim/admin-only six-digit confirmation challenge with 60-second expiry and one-wrong-
attempt invalidation; failed or expired confirmation performs no destructive change. Successful
Factory Reset invalidates every session, including developer-token sessions, with `factory_reset`,
but leaves developer-token configuration available for later authentication. Because Factory Reset
deletes every Known Device record and revocation tombstone, a credential presented afterward has no
matching record to classify against; the Bridge rejects it through the ordinary
unrecognized-credential/unpaired path, the same as a device that was never paired, not through the
`blocked`/`revoked` outcomes above.

## Trust administration surface

- Trust administration (list Known Devices, rename, revoke, block/unblock, forget, Reset Trust, and
  Factory Reset) is implemented once, as a
  reusable Bridge application-layer service (`TrustAdminService`) over `TrustStore`'s existing
  load/persist/revoke/reset/query boundary. No caller -- console, a future Flutter management UI, or
  developer tooling -- duplicates trust-store logic; each only formats input and output around the
  same calls.
- The five-digit `shortId` (never `clientId`, never a credential) is the identifier every
  administration surface accepts for "which Known Device" -- matching "Persistent local trust"'s
  own stated purpose for `shortId`: human-readable administration only.
- Native Skyrim has no supported API for registering a genuinely new console command. Confirmed by
  inspecting the vendored CommonLibSSE-NG headers: `RE::SCRIPT_FUNCTION` is a fixed, pre-populated
  table with no `AddCommand`, and intercepting the console's own script-compile call site requires
  disassembling the exact pinned game binary to find an undocumented call-site offset -- not
  something this project can determine safely or repeatably from source headers alone, and rejected
  for that reason (2026-08-16). The approved integration is instead a small, explicitly optional
  Papyrus console adapter, not a new native console-command mechanism:
  - **ConsoleUtil Extended** (a third-party SKSE plugin, `github.com/KrisV-777/ConsoleUtil-Extended`)
    parses in-game console text into named commands/subcommands/arguments from a YAML config and
    calls a matching Papyrus `global` function per subcommand. Its documented syntax
    (`commandName subcommandName -argumentName value`) covers `dovahlink list`,
    `dovahlink list trust`, `dovahlink list block`, `dovahlink help`,
    `dovahlink revoke -id <shortId>`, `dovahlink block -id <shortId>`,
    `dovahlink unblock -id <shortId>`, `dovahlink forget -id <shortId>`, `dovahlink reset-trust`
    (Reset Trust, immediate, no confirmation), `dovahlink reset` (starts the Factory Reset
    confirmation challenge; performs no mutation itself), and `dovahlink confirm-reset -confirm <code>`
    (confirms it, executing the destructive wipe only on a matching code) directly.
  - DovahLink Bridge registers a small set of native Papyrus functions
    (`SKSE::GetPapyrusInterface()->Register(...)`, the standard SKSE Papyrus-binding mechanism -- no
    memory patching, no offsets, version-independent) that a short Papyrus glue script forwards to.
    The glue script and ConsoleUtil Extended's YAML config are kept outside `bridge/`: they are not
    part of the native DovahLink core, only an optional way to reach it. Each native function does
    nothing but call `TrustAdminService` and return a formatted string; it owns no trust logic of its
    own. This is the approved, narrow exception to `ai/context/skse/architecture.md`'s "do not
    introduce Papyrus into the core bridge" rule -- the Papyrus surface is glue only, never policy.
  - ConsoleUtil Extended is an **optional runtime dependency of this one feature only**, not of
    DovahLink Bridge itself. The bridge attempts native Papyrus-function registration
    unconditionally (Papyrus mods are not introspectable from `SKSEPluginLoad`, so there is nothing
    to version-check at bridge startup); a registration failure is logged and remains isolated to
    this optional adapter; every other bridge behavior -- connection, pairing, trust
    persistence -- is entirely unaffected if ConsoleUtil Extended, the glue script, or its YAML
    config are absent. Without them, `dovahlink list`/`dovahlink list trust`/`dovahlink list block`/
    `dovahlink help` and the mutation commands are simply unrecognized
    console commands, exactly like any other unknown input; there is no error path and no degraded
    core behavior to account for.
- All callers use the same authoritative service and must preserve the ordering and event semantics
  above; no console, Flutter, or developer-tooling surface may implement a second trust authority.
  Existing implementation names such as `TrustStore::Reset()` must not be treated as permission to
  collapse Reset Trust and Factory Reset: Reset Trust is the non-destructive Trusted-to-Revoked
  operation, while the existing full wipe maps to the confirmation-gated Factory Reset behavior.

## Developer authentication

- The long-token capability from "Phase 1 exposure" becomes explicit developer authentication once
  pairing exists: it is enabled only when an explicit development token (`DOVAHLINK_DEV_TOKEN` or
  equivalent approved configuration) is configured, with identical behavior across debug, beta, and
  release builds — no configured token means no developer-token authentication path, in every build.
- Developer-token authentication is a separate provider from device pairing. It must not silently
  enroll the authenticating client into the persistent trusted-device store or create a long-lived
  paired-device credential.
- A developer-authenticated connection still obeys every applicable rule in this document: loopback
  restriction, input limits, protocol validation, a fresh `sessionId`, and the single-connected-client
  limit. Developer authentication is not a switch that disables security.
- A developer-token session is never treated as a Known Device by administrative Block or Revoke,
  including when its self-declared `clientId` happens to match a Known Device those operations
  target: the Bridge tracks whether the active session authenticated via `one_time_local_token`
  and exempts it from clientId-scoped disconnection on that basis, not merely from the hello-time
  `blocked`/`revoked` rejection checks. Factory Reset's unconditional session invalidation is
  unaffected by this exemption and still disconnects a developer-token session, per "Administrative
  session invalidation" above.

## Hello authentication and session trust tiers

`protocol/schema/README.md` intentionally left `hello`'s `auth.method` set and `hello_ack`'s
`clientIdentityKind` values open for "a future pairing phase" to fill in without changing the
message shape. This phase is that phase; this section is the filled-in decision.

- `hello.auth.method` accepts three values once pairing exists: `one_time_local_token` (developer
  authentication, unchanged from "Developer authentication" above), `unpaired` (bootstrap — no
  token; the client has no persisted credential yet and wants a session solely to run the pairing
  flow), and `trusted_device_credential` (the persisted credential a completed pairing issued,
  presented the same way `one_time_local_token` presents its token: hex text in `auth.token`).
  `unpaired` carries no `auth.token` field at all — there is nothing to present yet.
- A session admitted via `unpaired` is a real, fully authenticated `sessionId` (it still obeys
  loopback, input-limit, protocol-validation, and single-connected-client rules), but it is
  trust-restricted: until pairing succeeds on that connection, the message dispatcher accepts only
  `ping`, `capabilities`, `pairing_request`, and `pairing_confirm` from it — `subscribe` and
  `snapshot_request` are rejected the same way any other message outside the allowlist is,
  `malformed_message`, not a distinct error code. This mirrors `IsAllowedMessageType`'s existing
  allowlist mechanism in `bridge/application/message_dispatcher.cpp`; a session's trust tier is a
  second, narrower allowlist selector alongside "authenticated at all", not a parallel dispatch
  path.
- `hello_ack.clientIdentityKind` is `"unpaired"` for both developer-authenticated and
  bootstrap-`unpaired` sessions (no wire-visible difference; a developer-authenticated session is
  simply never trust-restricted, since developer authentication already implies full access per
  "Developer authentication" above) and becomes `"paired"` once a session is trusted — either
  immediately, in place, on that same connection, the moment its `pairing_confirm` resolves to a
  `trusted` or `already_trusted` outcome, or from the first message onward for a session admitted
  via `trusted_device_credential`. A client does not need to reconnect after pairing succeeds to
  start using the connection normally; reconnecting later without a code (`trusted_device_credential`)
  is a separate, subsequent event, per "Persistent local trust" above.
- Trust-tier upgrade happens exactly once, on the pairing state machine's own success, is never
  triggered by any other message, and is never downgraded except by the "Connection liveness"
  teardown path invalidating the session entirely (revocation of a *different* session's trust does
  not touch this one). There is no partially-trusted state beyond "restricted" and "full": a session
  is never allowed a third, intermediate message-type set.

## Connection liveness

- Once the WebSocket session is established, WebSocket-level Ping/Pong and a bounded idle timeout own
  connection liveness; do not invent a DovahLink-specific application heartbeat or infer liveness by
  guessing a TCP socket's state.
- Detect and recover from normal close, client process termination/crash, Bridge shutdown,
  transport/read/write failure, an unresponsive peer, and idle/heartbeat timeout through one
  deterministic teardown path: invalidate `sessionId`, cancel or finish outstanding I/O, close the
  transport, then release the connection slot. A dead `sessionId` can never become valid again.
- Audit the existing transport timeout implementation so established WebSocket liveness does not
  compete with an incompatible lower-level TCP-stream timeout owner. After the WebSocket is
  established, WebSocket timeout/liveness policy owns connection liveness; per-operation bounds use
  the appropriate explicit cancellation/deadline mechanism; lower-level connection/handshake
  deadlines may still apply before WebSocket ownership begins.
- During the single-connected-client model, prefer bounded short retry/backoff over same-client
  connection takeover: if a client crashes and reconnects quickly, the previous connection may still
  be completing teardown, and the reconnect attempt finds the slot temporarily busy rather than
  replacing the prior session's generation. Runtime tests must prove that normal close, force-
  close/crash, rapid restart, timeout, and Bridge restart all recover cleanly under this policy
  before same-client takeover semantics are considered.

## Local-OS-user threat boundary

- Persistent trust storage is scoped to the current Windows user, but loopback TCP itself is not
  proof of Windows-user identity. Per-user secure storage does not automatically make a loopback
  socket isolated from another simultaneously logged-in local account.
- The six-digit pairing proof, persistent credential authentication, rate limits, and loopback
  restriction remain the controls that apply to any connecting process on the same machine.
- If strict isolation from another simultaneously logged-in local OS account becomes a required
  threat boundary, it must be solved deliberately, with its own approved design, rather than assumed
  from `127.0.0.1`. Do not silently introduce a second IPC architecture to solve this before the
  threat model requires it.

## LAN gate

Before LAN exposure is approved, the design must define and test:

- endpoint identity and trust establishment
- short-lived, user-approved pairing
- authenticated encryption using an established protocol/library
- replay protection and session binding
- authorization for each message category
- revocation and reset behavior
- behavior when pairing fails or expires

Do not invent cryptography or treat an unencrypted pairing code as authentication. A pairing code may bootstrap trust only through an approved authenticated channel.

## Input limits

The transport rejects input before application decoding when it exceeds the approved limits:

- maximum frame size: 1 MiB
- maximum decoded nesting depth: 32
- maximum string length: 4 KiB
- maximum array length: 128 items
- maximum object members: 64
- maximum inbound messages: 100 per second per client
- maximum messages in one session: 10,000; the bridge closes the session before this bound is exceeded
- maximum connected clients during the first proof: 1
- handshake timeout: 5 seconds
- idle connection timeout: 60 seconds without a valid heartbeat or message
- bounded outbound queue: 128 messages per client, with 16 reserved control/recovery slots and 112
  data slots. The data slots contain keyed replaceable Snapshot entries and an ordered Event FIFO;
  Snapshot pressure may replace or defer an unsolicited value, while Event overflow closes the slow
  client rather than dropping an Event. The Bridge must also enforce a separate encoded-byte budget
  for queued data before Stage 4.2 production use; its numeric value is an implementation/profiling
  decision and any limit change requires the documented approval and rationale.

Limit changes require explicit maintainer approval and a documented reason.

## Failure behavior

- Reject malformed, oversized, unauthenticated, unauthorized, expired, replayed, and unsupported messages without invoking game code.
- Close immediately when framing or size validation fails before a safe message can be decoded; do not attempt to send an error over an invalid or oversized frame.
- Close a connection after 3 protocol violations within 30 seconds; do not retry invalid input indefinitely.
- Never disclose pairing secrets, authentication material, filesystem paths, or raw infrastructure exceptions in protocol messages.
- Log security failures with enough context to investigate, but emit at most one matching failure log per peer per second.
- A security failure must not block game-state capture or prevent clean plugin shutdown.

## Secrets and logging

- Keep pairing secrets and credentials out of source control, protocol fixtures, crash reports, and normal logs.
- Redact tokens, keys, and full peer credentials from diagnostics.
- Store credentials only through the approved trust-store and per-user secure-storage mechanisms defined under "Persistent local trust" above; do not invent persistence or cryptography of your own.
- Never log credentials, developer tokens, or other security-sensitive credential verifiers.
- Pairing codes are not written to normal persistent logs merely because they are short-lived.

## Session and replay protection

- To implement the server-issued session identity defined by `protocol/schema/README.md`, the bridge
  creates a fresh cryptographically random `sessionId` after successful token validation.
- The bridge binds the authenticated session exclusively to the socket that completed token
  validation. Disconnect, timeout, protocol-limit closure, or bridge shutdown invalidates the
  session before the socket is released; subsequent messages carrying that session ID are rejected
  before application handling.
- A session cannot move to another socket. Another socket presenting the same `sessionId` is
  rejected as `stale_session`.
- Under the first proof's one-client limit, another connection is rejected while the authenticated
  client slot is occupied; it does not replace the active session.
- To enforce the schema's unique `messageId` requirement, the bridge retains all seen IDs for the
  session; the 10,000-message session bound keeps this set bounded and prevents eviction-based
  replay.
- An expired token or invalid connection attempt is rejected before application handling.
