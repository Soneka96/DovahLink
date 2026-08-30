# 4.1/4.2 semantic audit

Stage 1 of `host/PLAN.md` requires that "every retained 4.1/4.2 semantic decision has an owner in
the new design, and every rejected decision has a recorded reason." This document is that audit. It
covers semantic/contract decisions from the existing 4.1 protocol (`ai/context/protocol/`) and 4.2
bridge implementation (`bridge/`, `ARCHITECTURE.md`) -- not `bridge/`'s internal C++ class shapes,
which are implementation detail Stage 2/3 design fresh against `ai/context/host/architecture.md`
and `ai/context/adapter/architecture.md`, not against `bridge/`'s existing classes.

Every decision below is marked **Retained** (owner in the new design), **Changed** (what changes and
why), or **Deferred** (still not decided, same as before). No decision in this audit is outright
rejected with no successor -- the migration relocates and, in a few cases, renames concepts; it does
not drop any of 4.1/4.2's security or reliability guarantees.

## Durable migration status

`host/PLAN.md` is the canonical, durable plan for this parallel migration. The
current branch has completed the host-side foundation and its security,
lifecycle, session, persistence, and documentation remediation through the
current remediation sequence's Step 7. Private IPC is now complete on both
sides (the host listener and the native adapter, including process launch,
mutual authentication, and lifecycle supervision). Live WebSocket hosting,
real capture, conformance, and final `bridge/` removal remain later work.

The existing `bridge/` tree remains the frozen behavioral reference until the
replacement passes the live conformance and runtime cutover gates. Nothing in
this audit authorizes deleting or silently replacing that reference.

## Identity model

- **`bridgeInstanceId`** -- **Changed.** The single native process it identified splits into two
  processes. The adapter owns the replacement internal `adapterInstanceId`, whose value changes on
  adapter restart. The host is a separate process lifetime with no public host identity; its OS
  process identifier is diagnostic only. Whether the public protocol exposes an instance identifier
  remains deferred to the later protocol revision stage.
- **`playContextId`** -- **Retained, split ownership.** The adapter detects and notifies play-context
  transitions (save/load), per its "Play-context transition notifications" ownership; the host owns
  `playContextId`'s authoritative identity and invalidates stale state and revisions on receipt of
  that notification, per its "authoritative published state, revisions" ownership. This is the same
  split `ARCHITECTURE.md`'s existing lifecycle-ownership section already implies for a Skyrim-facing
  signal that has client-visible consequences.
- **`clientId`** -- **Retained.** Owner: host. Pairing and persistent trust are entirely host-owned;
  the adapter has no role in client identity.
- **`sessionId`** -- **Retained.** Owner: host. WebSocket session lifecycle is entirely host-owned;
  each session is also bound to a host-owned per-socket `ConnectionId` and cannot be queried or
  invalidated through another connection.

## Persistent trust and pairing

- **Pairing state machine** (`NONE -> CHALLENGE_ACTIVE -> PENDING_CREDENTIAL -> TRUSTED` /
  `UNPAIRED -> PAIRING -> CONFIRMING -> TRUSTED`) -- **Retained.** Owner: host. The state machine
  itself has no Skyrim dependency; its only Skyrim-facing element, displaying the pairing code
  in-game, becomes the adapter's "Skyrim-facing pairing... notifications" duty, relayed from the host
  over the private IPC channel.
- **Recoverable confirmation handshake** (pending credentials do not authenticate an ordinary
  session; the client persists the credential before final confirmation; confirmation is idempotent;
  `pending_not_found` and `pairing_invalidated` remain distinct) -- **Retained.** Owner: host for
  pending state and trust commit, with the client owning its local recovery state. A host restart
  loses only the in-memory pending operation; an administrative mutation fences it and reports the
  distinct invalidation outcome.
- **DPAPI-based per-user trust storage** -- **Retained.** Owner: host. DPAPI's per-user scoping is a
  Windows OS mechanism independent of which process calls it; moving the trust store from `bridge/`
  to `host/` changes which process owns the file, not the persistence mechanism's validity.
- **Trust-store failure and atomic persistence** (missing file means empty store; malformed or
  undecryptable data fails closed; credentials remain in approved per-user secure storage; saves
  atomically replace the prior snapshot) -- **Retained.** Owner: host.
- **`shortId` / `displayName`** -- **Retained.** Owner: host, as part of host-owned pairing/trust.
- **Known Device states** (`Trusted`/`Revoked`/`Blocked`/`Unpaired`) and `session_invalidated` --
  **Retained.** Owner: host.
- **Revocation immediacy** (invalidate active session, close connection, reject credential reuse) --
  **Retained.** Owner: host, since sessions are entirely host-owned.

## Session and connection security

- **Phase 1 exposure** (loopback-only binding, one-time local connection token, atomic validation,
  rate-limited attempts) -- **Retained.** Owner: host, since WebSocket hosting moves wholly to the
  host. The loopback-only posture is unchanged until a LAN design is approved (see "Deferred items"
  below).
- **Developer authentication** (`DOVAHLINK_DEV_TOKEN` / `one_time_local_token`) -- **Retained.**
  Owner: host. A behavior-preserving relocation: the token is read at host startup instead of plugin
  load; every other rule in `ai/context/protocol/security.md`'s "Developer authentication" is
  unchanged.
- **Input limits** (frame size, nesting depth, string/array/object bounds, message rate, session
  message cap, outbound queue bounds, registered-area cap) -- **Retained.** Owner: host, since
  transport and queue ownership move wholly to the host. Stage 1 does not re-litigate the current
  numeric values; they carry forward as the baseline unless a later stage's profiling revises them,
  per `ai/context/protocol/security.md`'s own "Limit changes require explicit maintainer approval."
- **Connection liveness** (WebSocket Ping/Pong, idle timeout, one deterministic teardown path) --
  **Retained.** Owner: host.
- **Session and replay protection** (fresh `sessionId`, socket-bound, no session migration,
  `messageId` uniqueness) -- **Retained.** Owner: host.
- **Reconnect admission** (bounded retry/backoff instead of same-client takeover while a prior
  connection is still completing teardown) -- **Retained.** Owner: host.
- **Hello authentication and trust tiers** (`one_time_local_token`, `unpaired`, and
  `trusted_device_credential`; restricted unpaired allowlist; one-time upgrade to paired) --
  **Retained.** Owner: host. The host admits bootstrap sessions only for `ping`, `capabilities`,
  `pairing_request`, and `pairing_confirm`; developer-token sessions are wire-identified as
  `unpaired` but remain unrestricted and are not Known Devices for Block or Revoke. A session
  becomes paired only when the pairing state machine succeeds, never because of another message.

## Failure handling and diagnostics

- **Security failure behavior** (reject malformed, oversized, unauthenticated, unauthorized,
  expired, replayed, and unsupported messages before game code; close invalid framing immediately;
  close after three protocol violations in thirty seconds; do not block capture or shutdown) --
  **Retained.** Owner: host for public client traffic; adapter for its private-channel safety.
- **Secret and diagnostic handling** (keep pairing secrets and credentials out of source control,
  fixtures, crash reports, and normal logs; redact tokens, keys, and verifiers; never disclose
  filesystem paths or raw infrastructure exceptions; rate-limit matching security-failure logs;
  keep pairing codes out of persistent logs) -- **Retained.** Owner: host for public diagnostics
  and persistence; adapter applies the same redaction boundary to private-channel diagnostics.

## Trust administration surface

- **`TrustAdminService`** (list, rename, revoke, block/unblock, forget, Reset Trust, and Factory
  Reset) -- **Retained.** Owner: host. It remains one application-layer trust authority with no
  Skyrim dependency, and every caller preserves its mutation and invalidation ordering.
- **Reset Trust versus Factory Reset** -- **Retained.** Owner: host. Reset Trust converts only
  `Trusted` records to `Revoked`, destroys those credentials, cancels pending pairing, and leaves
  developer-token configuration and sessions unaffected. Factory Reset remains an independent,
  six-digit, confirmation-gated operation with one-wrong-attempt invalidation, failed/expired
  confirmation causing no mutation, and unconditional invalidation of every session.
- **Native console-command registration** -- **Changed.** The approach is rejected because the
  pinned CommonLibSSE-NG headers expose no supported command-registration API, while intercepting
  console compilation would require an undocumented offset in the exact game binary. The approved
  successor is the optional Papyrus/ConsoleUtil Extended adapter: the adapter owns only the Skyrim
  glue, registration failure stays isolated, absent glue leaves commands unrecognized, and the host
  remains the sole trust authority.
- **Papyrus console glue / ConsoleUtil Extended adapter** -- **Changed.** Owner: adapter, still
  optional. `TrustAdminService` itself moves out-of-process to the host, so the native Papyrus
  functions the glue script calls become IPC-relaying adapter components instead of direct in-process
  callers of `TrustAdminService`. The reason this surface exists (console-only trust administration
  without the companion app open) and its approval rationale in
  `ai/context/protocol/security.md`'s "Trust administration surface" are unchanged; Reset Trust
  remains immediate, while Factory Reset still requires the confirmation challenge.

## Compatibility model

- **Version-range compatibility** (no independent protocol-generation number; SDK declares a
  supported release-version range) -- **Changed.** Owner: host. Before cutover, clients check the
  frozen Bridge/mod release version; after cutover, the host release becomes the compatibility
  authority. This is more than a label change: the bootstrap, SDK range, fixtures, incompatible-
  peer behavior, and documentation must move together when the host becomes production. Stage 1
  records the ownership decision but does not edit `protocol/` itself.
- **Compatibility bootstrap handshake, unknown-data forward-compatibility rules, capabilities
  negotiation** -- **Retained.** Owner: host. Normal traffic and capabilities remain blocked until
  the host/client version check succeeds; unknown bootstrap values and incompatible peers fail
  clearly without fallback, stale data, or plausible defaults.

## Authoritative state, revisions, and publication ordering

- **One authoritative state store per state area per active play context, revision advances only on
  change** -- **Retained.** Owner: host, fed by capture values the adapter sends over the private IPC
  channel (`ai/context/adapter/architecture.md`'s "synchronous game-thread reads and bounded capture
  handoff").
- **Single per-state-area ordering point for apply/change-detect/revision-assignment** (4.2's
  `CaptureDispatchWorker` role) -- **Retained as a principle.** Owner: host. The ordering point must
  live wherever authoritative state and revisions live, which is now the host; the adapter's role
  shrinks to bounded, owned-value capture handoff only, with no application-level ordering decision
  of its own.

## Outbound delivery reliability

- **Bounded outbound queue design** (message and byte bounds, reserved control/recovery lane,
  Normal/Heavy data lanes, Snapshot latest-value-wins, reliable Event ordered and never dropped,
  overflow disconnects) -- **Retained.** Owner: host, since the outbound queue is client-session-scoped
  and sessions are entirely host-owned.
- **Recovery ordering** (a Snapshot establishes the new baseline; Events at or below it are
  superseded) -- **Retained.** Owner: host.

## Private host-to-adapter boundary

These are new-boundary decisions required by Stage 1 rather than reused public wire semantics:

- **Private IPC framing and versioning** (owned host/adapter messages, fail-closed incompatibility
  handling) -- **Changed.** Owners: host and adapter at their respective endpoints; the public
  protocol envelope is not reused as the IPC model. No protocol version is negotiated: host and
  adapter ship as one atomic package, so same-package peer-ownership and Skyrim-lifetime proof are
  the compatibility boundary instead (approved divergence D1,
  `plans/stage-3-thin-native-adapter-private-ipc/DIVERGENCES.md`).
- **Private IPC limits and access control** (bounded message/rate capacity, local-machine exposure,
  explicit authentication expectations, and no unbounded pipe) -- **Changed.** Owners: host for the
  listener and adapter for its client. Settled: a loopback-only TCP listener (no Windows ACL or
  named pipe), bounded frame size, outbound queue capacity, and per-connection message rate, with a
  mutually authenticated HMAC-based peer-ownership proof -- not a Windows access-control mechanism
  -- as the actual authentication boundary, per `ai/context/protocol/security.md`'s "Local-OS-user
  threat boundary".
- **Private IPC pressure and loss behavior** (game-thread capture never blocks on a full channel;
  host loss leaves the adapter safe; adapter loss makes host state unavailable; reconnect requires a
  fresh current-state baseline) -- **Changed.** Owners: adapter for bounded capture handoff and
  host for availability, state invalidation, and resynchronization.

## Known 4.2 limitations carried forward, not resolved by Stage 1

- **Stale-context publication is minimized, not eliminated** (`bridge/README.md`'s Stage 5 note) --
  **Changed in kind, not resolved.** The residual race changes shape because the play-context signal
  and the publication path now cross a process boundary (adapter to host, over IPC) instead of
  staying in one process. Full elimination still requires a send-time `playContextId` check against
  the live session's own current context; that remains later implementation-stage work (the
  equivalent of the old plan's deferral to its Stage 6 authenticated-session writer), not a Stage 1
  decision.
- **Reliable native-Event loss under capture-queue pressure is diagnosed, not prevented** --
  **Deferred, unchanged.** Still undecided pending a real production domain and its actual load,
  exactly as `bridge/README.md` already records. The new design inherits this same open question
  rather than resolving it speculatively ahead of a real domain.
- **The 2 MiB encoded-byte budget and 4 KiB Normal/Heavy threshold are unprofiled** -- **Deferred,
  unchanged.** Still provisional infrastructure values; they carry forward as the host's own starting
  baseline pending real domain-payload profiling.

## Adapter-only, no host interaction

- **`bAlwaysActive` / `bAchievementCompat` runtime compatibility toggles** -- **Retained.** Owner:
  adapter. Both are native engine-level patches/settings with no client-facing or session meaning;
  they belong entirely to the adapter's Skyrim-boundary ownership per
  `ai/context/adapter/architecture.md`.
- **Default loopback port `58231`** -- **Retained.** Owner: host, which now listens for clients in
  the adapter's place.

## Deferred items (unchanged by this migration)

- **Local-OS-user threat boundary and the LAN gate** -- **Deferred, unchanged.** Both were already
  explicitly deferred in 4.1/4.2, blocked until the maintainer approves a LAN design
  (`ai/context/protocol/security.md`'s "LAN gate"). Moving the implementing process from `bridge/` to
  `host/` does not change this decision; the same gate applies to the host once a LAN design is
  approved.
- **Future multi-contract protocol support** -- **Deferred, unchanged**, per
  `ai/context/protocol/compatibility.md`'s "Future multi-contract support."
