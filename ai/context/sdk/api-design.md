# SDK API design

Public-API conventions for the Dart Client SDK. Read `ai/context/sdk/architecture.md` first for the
one-engine/multiple-views rule this API surface sits on top of. Follow
[`ai/context/dart/dart-style.md`'s documentation rules](../dart/dart-style.md#documentation) for
Dartdoc symbol links and concise inherited-contract references.

## Simple-first API

The common developer experience hides boring reusable connection mechanics: raw WebSockets,
Ping/Pong, heartbeat implementation, ports once discovery/selection own them, credentials, secure
storage, session teardown, retry/backoff, revision recovery, snapshot reconciliation, stale-session
suppression, subscription recovery, and Host compatibility mechanics. The long-term simple
experience trends toward: find/select a DovahLink instance, pair if necessary, listen to typed state.
The SDK exposes authoritative local candidate discovery through
`client.pairing.discoverHosts()` and its replaying `client.pairing.candidates` stream. Discovery
probes the canonical loopback Host endpoint only; this does not provide LAN or mDNS discovery,
multi-instance selection, or automatic connection.

## Grouped client API

The supported client surface exposes views over the single `DovahLinkClient` engine. These groups
are API boundaries only; they do not create separate clients, service graphs, or mutable state
owners:

```text
DovahLinkClient
  hosts         durable Known Hosts and their runtime projection
  connections   one active session's connection and authentication lifecycle
  pairing       discovery candidates and pairing operations
  currentHost   typed game state for the admitted active Host
```

The public operations are:

| Group | Public surface | Owner and semantics |
| --- | --- | --- |
| `hosts` | `loadKnownHosts()`, `knownHostsChanges`, `knownHostStatesChanges` | `ClientStateService` and `HostAvailabilityService`; durable metadata and runtime availability/session projection remain distinct. |
| `connections` | `connectCandidate(uri)`, `connectKnownHost(hostId)`, `renameDevice(displayName)`, `disconnect()`, `state`, `stateChanges`, `initialConnectionRetryChanges`, `invalidationReason`, `knownHostInvalidations` | `SessionService`, `AuthenticationService`, `ReconnectService`, and `RequestService`; each discrete invalidation event carries the exact Known Host ID and Host-reported reason together. Events are not replayed. Each connect operation includes authentication and session admission. Initial retries do not enter established-session `reconnecting` state. `renameDevice` requires the active trusted session and affects only the Client trust record on that Host. |
| `pairing` | `candidates`, `discoverHosts()`, `authenticateCandidate(uri)`, `authenticateKnownHost(hostId)`, `requestCode()`, `renotify()`, `cancel()`, `confirmCode(...)`, `recoverPendingPairing()` | `PairingService` composes connection admission with pending-confirmation recovery and sequences confirmation plus credential acknowledgement. Candidates remain runtime-only. |
| `currentHost` | Admitted Host/session context, typed game-state streams, and subscription operations | Existing session, state trackers, and `SubscriptionService`; retain per-domain replay, synchronization, error, and recovery behavior. |

The group names describe SDK concepts, not Flutter's visually selected Host. `connections` reports
the SDK's active transport/session lifecycle; `currentHost` refers to the Host admitted by that
session. A candidate or a UI selection is not an admitted Host. No grouped operation may infer
trust from discovery metadata or use a Known Host credential based only on a candidate's Host-ID
claim.

`connections.renameDevice(displayName)` sends the typed `rename_request` for the current trusted
session and returns the Host's typed `RenameOutcome`. It cannot rename an offline Known Host or
fan a change out across saved Hosts; each trust record belongs to its own Host. The operation is
not retried after an ambiguous transport failure because the Host persists the trust mutation and
advances its security fence. An empty name follows the protocol's clear-name behavior.

`connections.renameDevice(displayName)` sends the typed `rename_request` for the current trusted
session and returns the Host's typed `RenameOutcome`. It cannot rename an offline Known Host or
fan a change out across saved Hosts; each trust record belongs to its own Host. The operation is
not retried after an ambiguous transport failure because the Host persists the trust mutation and
advances its security fence. An empty name follows the protocol's clear-name behavior.

The Flutter app maps these grouped values into Redux and retains navigation, dialog lifetime, user
input, and display decisions. `ReconnectService` owns the three-second initial retry schedule and
cancellation separately from its bounded established-session recovery. It reports the active
initial-retry intent through `connections.initialConnectionRetryChanges`; Flutter keeps the pairing
and Known Host presentation Offline while that intent is active. `pairing.authenticateCandidate` and
`pairing.authenticateKnownHost` sequence the shared connection/authentication path with pending
confirmation recovery. `pairing.confirmCode` persists the issued credential and completes its Host
acknowledgement as one SDK operation. Flutter retains user-facing wording, failure-to-presentation
mapping, navigation, and dialog lifecycle.

The current owner boundaries are:

| Concern | Owner and public view | Invariant and regression risk |
| --- | --- | --- |
| Known Host persistence and candidate reconciliation | `ClientStateService` and `DovahLinkClient`, exposed through `hosts` and `pairing` | Preserve storage errors, deterministic snapshots, race suppression, and the rule that candidate claims never select credentials. |
| Known Host availability and session projection | `HostAvailabilityService` and `SessionService`, exposed through `hosts` | Preserve replay, recovery after stream errors, runtime-only availability, and exact relationship matching. |
| Connection/authentication and recovery | `SessionService`, `AuthenticationService`, and `ReconnectService`, exposed through `connections` | Transport-open is not session admission. Initial retries remain separate from bounded established-session recovery. |
| Pairing protocol sequencing | `PairingService`, exposed through `pairing` | Preserve crash recovery, retriable wrong-code/pacing outcomes, expiry, cooldown, credential rejection, administrative invalidation, and candidate/Known Host credential boundaries. |
| Redux and presentation | Flutter middleware mirrors grouped SDK projections | Flutter owns selection, wording, navigation, and dialog lifecycle; it does not implement retry policy or sequence protocol operations. |
| SDK composition and shutdown | `app/lib/injection_container.dart` creates one client; `AppShutdownService` closes it | Every group references that engine, and shutdown remains idempotent with no late state publication. |

No separate SDK component construction was found in Flutter production code. `PairingRemoteDataSource`,
repositories, and use cases form application boundaries; remove one only after its behavior has moved
and its consumer tests prove that no presentation-independent mapping or orchestration remains.

The in-repository Flutter application and SDK tests are the known consumers. The SDK is not
published as a stable public package and has no publication workflow; the grouped API is the
supported repository surface, with no duplicate root aliases. The wire protocol and supported Host
compatibility range do not change.

The grouped API does not authorize multiple active sessions, multi-Host game-state fetching, a new
protocol version, SAS, or changes to Host/Adapter behavior.

## Expert capabilities

Advanced developers may inspect lifecycle and diagnostic information: connection state, connected
Host version, SDK/Host compatibility result, Host `hostId`, OS-derived `hostName`, current
`stateAuthorityId`/`playContextId`/
`sessionId`, capabilities, revision/recovery diagnostics, subscription diagnostics, structured
connection/recovery events, and supported administration operations. "Advanced" must not mean
"bypass invariants": an expert API still preserves contract validation, session safety, lifecycle
correctness, security rules, state ownership, and compatibility rules. Do not expose the raw socket
merely because an expert API exists, unless a later explicit low-level API decision approves it.

`HelloResult.hostId` and `HelloResult.hostName` are values reported by the connected peer. The
`hostId` represents the stable DovahLink Host installation identity the peer asserts; the name is
mutable computer-name display metadata. Neither represents the endpoint. A peer's assertion of
`hostId` is not cryptographic proof that it owns a previously trusted identity.

`client.hosts.loadKnownHosts()` returns the complete immutable Known Hosts collection, ordered by
`hostId`; `client.hosts.knownHostsChanges` emits that same complete view on listen and after each
committed semantic change. A load failure is reported as a stream error, never converted to an empty
collection. The same subscriber remains attached and receives state after a later successful SDK
load or mutation. Each public `DovahLinkHost` contains identity and last-known metadata only; it
exposes no credential and does not claim the Host currently trusts this client. Known Host
authentication takes a `DovahLinkHostId`; the SDK resolves its current endpoint and Host-scoped
credential. Candidate authentication takes an endpoint and never selects Known Host credentials.
Trusted sessions may refresh metadata only for the matching Known Host ID; discovery claims never
refresh persisted metadata. SDK-owned Host IDs are stored and compared in canonical lowercase form;
the typed `DovahLinkHostId` accepts either UUID casing at its boundary.

`client.pairing.discoverHosts()` returns the complete immutable collection of current candidates,
ordered by normalized Host ID. The SDK removes every claim whose ID belongs to the latest committed
Known Host collection, keeps candidates in runtime memory only, and updates `client.pairing.candidates`
when discovery or a committed Known Host change changes membership. Each successful discovery
reconciles against the latest persisted state; a stale asynchronous result cannot reintroduce a
Known Host. A storage/load failure is surfaced instead of treating an unverified collection as a
successful reconciliation. Empty or failed discovery does not delete persisted Known Hosts.

`DovahLinkDiscoveryService.discover()` uses [IHostPresenceProbe] to query the sessionless local Host
metadata endpoint and returns its validated Host claim to the SDK client for reconciliation. The
response's `hostId`, `hostName`, and `hostVersion` are validated for identity shape and Host
compatibility. In [DovahLinkHost], `hostId` is the stable installation identity the peer claims,
`hostName` is mutable display metadata, and `endpoint` is the current location. The claim does not
authenticate Host identity or prove the peer owns an identity previously trusted under that ID. A
discovered `hostId` alone must never authorize trust, credential disclosure, pairing bypass, or
another security-sensitive decision. An unreachable or timed-out endpoint returns no claim; an HTTP
rejection preserves its status in `DovahLinkConnectionException`, while malformed metadata and
incompatible Host versions remain typed failures. The probe sends no credential and never creates a
protocol session.

## No duplicate stacks, no speculative surface

Do not build a second connection/service stack behind a different API tier — see
`ai/context/sdk/architecture.md`'s one-engine rule. Do not add a public API for a capability that
does not exist yet, and do not add a raw-transport escape hatch without explicit maintainer
approval.

## Curated public exports

Consumers import a small supported public library surface. Internal transport, codec, persistence,
compatibility, and state-machine classes are not accidentally exported; a third-party developer
should not need to import an internal file to use a supported feature, and the implementation
folder structure is not itself the public contract. Public SDK publication (pub.dev), package
stability guarantees, and a public release workflow are separate future decisions — do not publish
merely because the package exists, and do not treat repository-internal status as a reason to skip a
curated public API.

## Typed errors, app-owned wording

The SDK converts infrastructure/contract failures into typed semantic client failures/events (for
example: pairing code expired, client/device revoked, Host unavailable, incompatible Host
version, connection lost, recovery failed, state unavailable) rather than freezing exact type names
speculatively. The SDK owns typed meaning; the app owns user-facing wording and presentation. The
app must not parse raw socket exceptions or diagnostic strings to determine product behavior, and
the SDK must not return product-specific UI strings. For incompatibility, the SDK provides enough
structured information for the app to distinguish "Host is older than supported" from "Host is
newer than supported" when that is safely knowable, per
`ai/context/protocol/compatibility.md`.

Administrative session invalidation is exposed as typed semantic information for SDK consumers:
`revoked`, `blocked`, `trustReset`, and `factoryReset`. These reasons are not durable authoritative
trust state and must not leak as raw string comparisons throughout consumers. The official Flutter
app may map all four to one generic unavailable/disconnected presentation, while third-party SDK
consumers remain free to inspect or display the precise reason.

## Protocol DTO decoding

Protocol DTOs in `lib/src/protocol/` use standard `json_serializable` with generated `.g.dart`
files — including outgoing, encode-only DTOs; a DTO is not hand-written merely because it is
simpler to construct directly. Every canonical wire value with a finite vocabulary gets its own
typed enum field, including envelope `messageType` and `error.code`, annotated per member with
`@JsonValue` for its wire name; an unrecognized value fails inside that DTO's `fromJson` as a
`ProtocolFormatException`, never silently accepted or defaulted. Do not use
enhanced enums with a `wireValue`-style property, and do not add an enum extension that duplicates
the wire mapping `@JsonValue` already expresses — an enum extension may still add semantic
behavior unrelated to wire mapping (for example `hasActiveChallenge`). Do not reach into another
DTO's or enum's generated private map from a different library; each DTO's own generated
`fromJson`/`toJson` is the only place that mapping is used. Credentials, versions, and fields the
canonical schema explicitly declares open-ended stay `String`; canonical message types and error
codes do not.

A `ProtocolFormatException` from DTO decoding is translated back into the SDK's existing public
`DovahLinkProtocolException(code: 'malformed_message', retryable: false)` at the point each DTO is
consumed, so an unrecognized wire value keeps producing the same typed exception SDK consumers
already handle — decoding moving into generated code changes where the check happens, not what a
consumer catches.

`ai/context/dart/dart-style.md`'s one-type-per-file rule applies to protocol DTOs without
exception: a payload with a nested object (for example `hello`'s `auth`) gets a separate DTO class
in its own file for that nested shape, never inlined as a second class alongside its parent.

## Subscription intent versus mechanics

A consumer expresses intent ("I want player state"); the SDK owns whether satisfying it currently
requires a new remote subscription, reuse of an existing one, an initial snapshot, reconnect
recovery, resubscription, revision-gap recovery, play-context invalidation, or stale-state
suppression. The app must not maintain a competing protocol-level truth (for example a boolean
tracking whether the server is subscribed); it may know a screen currently wants the state, but the
SDK knows whether the remote subscription and recovery state are actually valid.

An SDK consumer subscribes and unsubscribes explicitly per domain; subscription must never be
inferred from whether a Dart Stream happens to have listeners. `unsubscribe` for a domain tells the
Host to stop traffic for that domain/client rather than only detaching the local listener. After
ordinary reconnect, the SDK restores previously desired subscriptions automatically. After
administrative invalidation, desired subscriptions remain remembered but stay dormant — the SDK
does not reactivate them until an explicit user-initiated Retry succeeds, mirroring the
credential/reconnect policy in `roadmap/03`'s Phase 3.3. An explicit SDK disconnect clears desired
subscription intent; ordinary transport loss and administrative invalidation preserve it.

## SDK domain types and app presentation values

Reusable typed DovahLink client/domain concepts belong to the SDK. The Flutter app maps SDK outputs
into Redux state, Redux-backed ViewModels, immutable ViewData, or localized user-facing
representations. Third-party SDK users must not need to depend on official-app domain entities,
Redux, Flutter ViewModels, or product UI concepts, and official-app presentation architecture must
not accidentally become the SDK's public API.

## Security

Do not duplicate `ai/context/protocol/security.md` here; obey it. The SDK never logs credentials or
developer tokens, never persists secrets insecurely, never turns a security-sensitive failure into
plausible success or default state, never bypasses authentication through an "advanced" API, and
never accepts a stale or foreign session for convenience. Any security-semantic change still
requires its own approved architecture/security decision.

## Request retry safety, session requirement, and timeout class

Every SDK request/operation carries three independent properties, not one combined enum:

- Retry safety — `retrySafe` or not. `retrySafe` means repetition cannot produce an incorrect
  duplicate effect, not that it retries indefinitely; a `retrySafe` operation may be retried once
  automatically after reconnect, and a repeated failure is surfaced rather than retried again. A
  non-`retrySafe` operation whose response is lost must not be automatically re-sent — the SDK
  cannot know whether the Host already executed it. This is a transport-level property, unrelated
  to any future Host-side command idempotency/replay-protection design.
- Session requirement — the connection/trust state an operation requires (connected, unpaired,
  trusted, ...), expressed with the existing trust/session concepts rather than a new privilege
  layer. A queued operation that survives reconnect is revalidated against the new session state
  before it is sent; if no longer valid, it is not sent and fails with a typed SDK error instead.
- Timeout class — a small set of centralized bounded timeout categories (short/normal/heavy) rather
  than an arbitrary literal per call site. A timed-out request fails, the connection is treated
  unhealthy, and recovery follows the applicable bounded reconnect behavior; a timed-out request is
  never silently followed by sending the next queued request as if nothing happened.

This model applies now, independent of whether requests execute concurrently. Phase 3.3
(`roadmap/03`) classifies its own operations against this model now; Stage 5 (`roadmap/05`) extends
coverage to the rest of the SDK's operations as they are built.

## New-subscriber state replay

A new subscriber to a typed SDK stream that represents current state receives the current value
immediately when one is already known — this applies to lifecycle state and future
current-state-bearing domain views. It does not imply replaying historical events on Event-mode
streams; a late subscriber to an Event-mode domain still synchronizes through that domain's normal
initial-snapshot path, not through event replay.

`client.hosts.knownHostStatesChanges` is the complete runtime projection of durable Known Hosts,
their `DovahLinkHostAvailability`, and their exact-relationship `DovahLinkKnownHostSessionState`.
When storage provides a snapshot, it immediately provides the
current immutable collection, ordered deterministically by Host ID, then emits a complete replacement
when Host metadata, availability, or session state changes. Session state is connected only after
successful session admission for that durable Host ID; candidate claims never associate a session
with a Known Host. Equivalent snapshots are suppressed, except the
first complete snapshot after a stream error, which signals recovery even if its values are
unchanged. An initial storage failure is reported to the subscriber, which remains attached for
later recovery.
Availability is runtime-only; it is not part of `DovahLinkHost` or persisted client state. The SDK
starts bounded Known Host presence checks when a client is created. Startup, new Hosts, and
endpoint changes may emit `checking`; periodic refresh retains the previous availability
while a probe runs, then publishes `online`, `offline`, or `unknown` when evidence arrives. Keep
`knownHostsChanges` for consumers that need durable Host metadata without runtime availability.

`client.connections.disconnect()` ends the current protocol session without clearing Known Host
reachability evidence, and leaves presence monitoring active. `DovahLinkClient.close()` is the
terminal lifecycle operation that stops the monitor, cancels its timer and probes, closes its Known
Host observation subscriptions, and disconnects the current session.

Commands and authoritative state are separate API views. A command may report whether its operation
was accepted or rejected and return operation-specific metadata, while the resulting persistent,
session, trust, pairing, or game state is observed through its owning typed API or stream. Consumers
must not invent the expected state transition from command success.
