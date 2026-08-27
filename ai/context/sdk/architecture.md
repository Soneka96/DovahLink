# SDK architecture

These conventions govern the Dart Client SDK (`sdk/dart/dovahlink_client/`, once it exists — see
`sdk/README.md` and `roadmap/05-dart-client-sdk-foundation.md`'s Phase 5). Shared Dart-language rules live in
`ai/context/dart/dart-style.md`; do not duplicate them here.

## Dependency direction

```text
protocol/
   ↓
Dart SDK implementation
   ↓
Official Flutter app (and any other Dart consumer)
```

`protocol/` remains the sole canonical language-neutral Bridge/client contract. The SDK implements
that contract for Dart consumers; it is not a second protocol authority, and its convenient typed
client/domain models never become wire-contract authority. Bridge-version compatibility ownership
(the SDK's declared supported range, the compatibility bootstrap, contract-change assessment) is
defined by `ai/context/protocol/compatibility.md`; do not restate it here, and do not give the SDK
an independent historical protocol-generation model.

## Bridge versus SDK ownership

The Bridge remains authoritative for live Skyrim game state, authoritative revisions, the current
`playContextId`, server-side trusted-client records, revocation, trust administration, Bridge
capabilities, and server-side security decisions. The SDK is a client library: it must never become
authoritative over Skyrim or server-side trust.

Trust has two sides, and the SDK owns only its own:

```text
Bridge:            "These clients are trusted."
SDK on a client:    "This is my clientId and credential."
```

The SDK owns its local `clientId`, credential, pairing `CONFIRMING` recovery state, and other
client-side authentication persistence. It may expose typed APIs for Bridge trust-administration
capabilities (list/revoke/reset), but the authoritative mutation always happens on the Bridge; see
`ai/context/protocol/security.md` for the trust model itself.

## App independence

After the Dart Client SDK Foundation phase, the official app depends on the SDK's public API for
normal DovahLink communication. It must not construct a new parallel raw WebSocket implementation,
Bridge compatibility implementation, protocol decoder, authentication implementation, pairing
implementation, reconnect state machine, revision tracker, or subscription engine. If the app needs
behavior the SDK cannot provide, treat that as evidence the SDK needs to be extended, not as
justification for private app-side plumbing.

## One engine, multiple API views

The SDK has one underlying client engine/state machine. Do not create separate implementations
(for example `SimpleDovahLinkClientService` alongside `AdvancedDovahLinkClientService`) that each
establish their own transport connection, state store, subscriptions, session, or cache. Expose a
small high-level API plus focused expert capability interfaces (lifecycle, diagnostics,
administration, compatibility metadata) over the same underlying client instance. Do not pre-create
an empty capability interface for a hypothetical future need; add one when a real approved
capability exists. Avoid letting one unbounded "advanced" interface become a dumping ground.

## Public versus internal boundaries

The reusable client core must not depend on Flutter widgets, Redux, `GetIt`, navigation, or any
official-app presentation architecture — see `ai/context/sdk/api-design.md` for the curated public
API this boundary supports.

## Platform ports

Where reusable client behavior requires platform-specific facilities (secure credential storage,
cache/filesystem location, future local discovery, platform lifecycle integration), place them
behind explicit ports/interfaces rather than hardcoding one platform's APIs into the reusable client
model. Do not pre-create speculative adapters; add one when a real supported platform requires it.
An initial Windows implementation may use Windows-appropriate facilities without those APIs becoming
part of the reusable model; later Android/iOS implementations provide platform behavior without
rewriting connection, authentication, or state semantics.

## Feature and capability organization

Organize the SDK around the capabilities it actually implements (connection/session,
compatibility, pairing/trust, subscriptions/state, persistence, cache) rather than a generic
"services" layer. Each capability's internal implementation stays internal; only its curated public
surface is exported, per `ai/context/sdk/api-design.md`.

File organization (one type per file, with the enum/constant exception) is a shared Dart-wide rule;
see `ai/context/dart/dart-style.md`, not a restatement here.

## Inbound message handling

The SDK owns exactly one inbound reader over the transport's message stream; individual methods
must not each consume "the next incoming message" as their own reply. The reader maintains a table
of pending operations keyed by the outgoing `messageId` each generates, and matches a Bridge reply
to its operation by the reply's `correlationId` — never by arrival order, message type, or timing.
A message whose `correlationId` is `null` is unsolicited and is routed to the appropriate typed
unsolicited handler or SDK surface according to its message type. It must never be consumed as the
response to a pending operation. A message whose `correlationId` is non-null must match a pending
operation's outgoing `messageId`; a non-null `correlationId` that matches no pending operation is a
protocol violation, not an unsolicited message — the SDK fails closed (a typed protocol error,
connection treated unhealthy) rather than reinterpreting it by message type or arrival timing. This
pending-operations table does not itself require concurrent request dispatch: the SDK may keep
requests serialized/queued internally now and still gain full correctness from correlation, with
concurrent execution left as a later, independent decision. Every future typed domain stream
(player state, inventory, quests, and others as they are added) is layered on this same single
receiver; it must not grow into one undifferentiated public stream — see
`ai/context/sdk/api-design.md`'s curated-exports rule for how domain-specific surfaces stay
separate.

## Internal composition

The client engine described above is implemented as seven major Services, each an
independently-testable behavioral subsystem, plus supporting collaborators. Every supporting
collaborator that owns behavior has its own explicit contract; data-only helpers remain concrete.
**A concrete production Service implementation implements exactly one architectural Service
interface.** One concrete class never carries multiple architectural identities through multiple
`implements` clauses of Service-shaped interfaces — a consumer's constructor should never need to
learn that two differently-named dependencies are secretly the same object. (This rule does not
apply to the pre-existing, orthogonal platform-port category — `IDovahLinkTransport`/`IClientStorage`
— which is unaffected by it; see "Platform ports" above.)

This rule allows no exception, including the case where a `ServiceImpl` is the sole genuine owner
of some state a collaborator needs — "it's the only real owner, no other object could stand in" is
not by itself grounds for the `ServiceImpl` to implement that collaborator's dependency port itself
and pass `this`. `ConnectionTeardownCoordinator`, `MessageRouter`, and
`PendingOperationTransmitter` are behavior-bearing supporting collaborators, so each has its own
contract and consumers depend on that contract. None of them implements a `ServiceImpl`'s own
interface, and no `ServiceImpl` implements one of their dependency shapes on itself.

When a supporting collaborator's dependency shape does not already match a real object a
`ServiceImpl` holds, extract the owning state and logic into a behavior-bearing supporting object
with its own contract and depend on that contract. Never let a `ServiceImpl` implement the
collaborator's own dependency port and pass `this`.

The seven Services:

- `ISessionService`/`SessionService` — owns transport lifecycle, connection state, and stream
  ownership: `connect`, `disconnect`, reads (`connectionState`, `currentSessionId`,
  `currentTrustState`, `invalidationReason`), and the reactive signals `onUnhealthy`,
  `onProtocolViolation`, `onSessionInvalidated`, `onUnsolicitedError`. Privately owns
  `ConnectionTeardownCoordinator` and `LifecycleOperationQueue`.
- `ISessionAdmissionService`/`SessionAdmissionService` — `admitSession`, a privileged capability
  injected only into `AuthenticationService`. Also triggers `RequestService`'s
  retry-orphaned-operations transition as part of admitting a session, keeping reconnect/session
  recovery cohesive in one place.
- `ISessionTrustService`/`SessionTrustService` — `markTrusted`, a privileged capability injected
  only into `PairingService`.
- `IRequestService`/`RequestService` — owns pending requests, timeout policy, retry behavior,
  envelope decoding, correlation, and unsolicited routing: `sendAndAwait`, `handleIncoming`,
  `failAll`, `retryOrphanedOperations`. Privately owns `MessageRouter` and
  `PendingOperationTransmitter`.
- `IAuthenticationService`/`AuthenticationService` — `hello`/authentication and credential
  recovery.
- `IPairingService`/`PairingService` — pairing operations.
- `IReconnectService`/`ReconnectService` — bounded automatic recovery from ordinary transport
  loss, reconnecting and re-authenticating up to an attempt budget and a hard deadline without
  taking over transport or authentication state from `ISessionService`/`IAuthenticationService`.

Each interface and its implementation are named classes in their own files under `src/`, absent
from the public barrel per `ai/context/sdk/api-design.md`'s "curated public exports".

### Not everything is a Service

A Service split corresponds to a meaningful behavioral responsibility or a real authority boundary
(who is allowed to do what) — never merely to a field or method count. Do not create a micro-service
such as `SessionIdService` or `ConnectionStateService` that only exposes one mechanical operation or
mirrors one field; that recreates the discoverability problem this design displaced, under a
different naming scheme. Supporting/mechanical behavior keeps a plain, direct-noun name, but any
behavior-bearing class still follows `ai/context/common.md`'s mandatory abstraction rule. DTOs and
other data-only types remain without artificial service interfaces; validators, codecs, and leaf
policies are behavior-bearing when they make decisions and therefore require their own contracts.

### Naming

Major capability: `IXService` (interface) / `XService` (implementation), per
`ai/context/dart/dart-style.md`'s `I`-prefix interface-naming convention -- the implementation keeps
the bare capability name rather than an `Impl` suffix. Supporting object: direct domain/mechanical
noun; if behavior-bearing, it still has an explicit contract and concrete implementation without
requiring the `I`-prefixed name pair. Avoid arbitrary `Manager`, `Handler`, `Controller`, `Provider`,
`Port`, `Gateway`, `Facade` unless the word communicates a genuinely distinct role; `Coordinator`
remains acceptable for a real sequencing collaborator (`ConnectionTeardownCoordinator`) because
ordered, generation-checked sequencing is a genuinely different shape from a Service's
request/response or command/report shape.

The seven Service interfaces (`ISessionService`, `ISessionAdmissionService`,
`ISessionTrustService`, `IRequestService`, `IAuthenticationService`, `IPairingService`,
`IReconnectService`) and the two platform ports (`IClientStorage`, `IDovahLinkTransport`, including
the two public exports) were renamed to this convention as an intentional pre-1.0 breaking API
migration, since the SDK has no supported public release yet (`ai/context/common.md`'s "Pre-release
compatibility"). Each Service's implementation dropped its `Impl` suffix in the same migration (for
example `SessionServiceImpl` became `SessionService`), so the bare name is no longer available for
anything but the concrete implementation. No compatibility shim or deprecated alias was added for
the previous unprefixed interface names or the removed `Impl` suffix. This is a naming migration
only: every renamed type's existing responsibilities, collaborator set, and consumer wiring from
Phase 3.3 are unchanged.

### Dependency injection

Constructor injection is the default and, aside from the three named callbacks below, the only
wiring mechanism. No service locator, no static/global dependency lookup, no hidden singleton
access inside a Service implementation. A single Service instance may legitimately be injected into
multiple consumers — `IRequestService` is injected into `SessionAdmissionService`,
`AuthenticationService`, and `PairingService` — that is expected and does not duplicate the
Service or its state.

No exceptions for privately-owned collaborators. A class never constructs its own dependency —
however small, however single-consumer, however free of its own dependencies (a plain queue, a
one-line delegating adapter) — inside its own constructor body. Every collaborator a class needs,
without exception, is a constructor parameter the caller supplies already built. This is what
"privately owned" means throughout this document: sole-consumer by ownership, not exempt from an
explicit contract when behavior-bearing, and never internally self-assembled. The payoff is that
exactly one place ever calls a constructor with
`new`-shaped intent for the whole graph — today `DovahLinkClient`, later a generated or hand-rolled
injection container — and every test can substitute any single collaborator, at any depth, without
the class under test needing a special test-only constructor to make that possible.

Every behavior-bearing SDK class or equivalent type has an explicit interface, and every consumer
depends on that interface even when only one implementation exists. This rule is adopted
phase-forward and does not reopen completed phases. The callback exceptions below are lifecycle
inversion points, not permission to bypass the rule for ordinary collaborators.

### Callbacks

Exactly three late-bound, function-typed, nullable fields exist on `SessionService`, each
because a named constructor dependency in that direction would create a genuine construction-order
cycle:

These are the SDK's explicitly enumerated lifecycle-inversion exceptions to ordinary constructor
injection. They are typed, assigned once by the composition root, and do not construct or resolve
implementations.

1. **Teardown notification** → `IRequestService.failAll`. `SessionService` can detect a
   connection failure entirely internally (its own transport subscription's `onError`/`onDone`) and
   must trigger pending-operation failure/orphaning after tearing down, but cannot hold an
   `IRequestService` reference, because `RequestService` is constructed after `SessionService`
   and itself depends on `ISessionService`. Assigned once by the composition root as a method
   tear-off (`sessionService.onTeardown = requestService.failAll`), not as
   `RequestService` implementing a second interface. Gated by the same generation check
   `ConnectionTeardownCoordinator` already uses internally, so a duplicate `onError`+`onDone` signal
   for one dead connection fires the callback exactly once, never twice, and a later, genuinely new
   teardown still fires it again.
2. **Ordinary transport-loss notification** → drives `ReconnectService`'s recovery start. Same
   reasoning: `ReconnectService` needs `ISessionService` and `IAuthenticationService` as
   constructor dependencies, so `SessionService` cannot hold a matching `IReconnectService`
   reference without a cycle.
3. **Incoming-message forwarding** → `IRequestService.handleIncoming`. `SessionService` owns
   starting the transport's inbound subscription (`connect()`'s own implementation, per "Request/
   session boundary" below) and is the only class that ever sees a raw inbound message land, but
   decoding, correlation, and unsolicited routing belong to `RequestService`, and the same
   construction-order cycle applies: `RequestService` depends on `ISessionService`, so
   `SessionService` cannot hold an `IRequestService` reference. Assigned by the composition root
   alongside `onTeardown` (`sessionService.onIncomingMessage = requestService.handleIncoming`).
   `SessionService`'s own subscription handler discards a message from an already-superseded
   connection generation before this callback ever runs, so a stale message never reaches
   `RequestService`.

Callbacks are reserved only for lifecycle/event inversion required to break a genuine construction
cycle — never a general substitute for constructor injection. If a fourth callback appears to be
needed, that is a signal to stop and re-derive the dependency graph, not to add it.

### Request/session boundary

`RequestService` depends on `ISessionService` (a normal constructor dependency, not a callback —
there is no construction cycle in this direction) for two reasons. First, reads: `sendAndAwait`
checks `ISessionService.connectionState` before transmitting and fails immediately with a typed
`DovahLinkConnectionException` unless it is `connected` -- with one narrow exception: `hello`
itself is admitted while `reauthenticating`, since that is precisely the message a bounded-recovery
attempt sends to find out whether this device is trusted, blocked, or revoked, and it cannot itself
wait for trust to already be confirmed. No other message type is ever exempted. `reconnecting` fails
outright, with no exception even for `hello`: it spans a whole bounded-recovery cycle (backoff
delay, then an in-flight `connect()` attempt) during which no transport is guaranteed usable at all.
Once the transport reconnects, the attempt moves to `reauthenticating` -- transport up, trust not
yet confirmed -- and `connectionState` only reaches `connected` once that attempt's `hello` actually
admits a session. An already-pending `retrySafe` operation an earlier ordinary transport loss
orphaned is a separate mechanism, unaffected by this guard: `IRequestService.retryOrphanedOperations`
retransmits it directly, bypassing
`sendAndAwait` entirely, only after `SessionAdmissionService` admits a fresh session
post-reconnect (see "Internal composition" above). Starting the transport's
inbound subscription is fully private to `SessionService.connect()`'s own implementation and is
never exposed on `ISessionService`'s interface: a successful `connect()` already guarantees receiving
is active for the connection's whole lifetime, so the `connectionState` check alone is enough to
guard a caller that sends before `connect` was ever called, without leaking receiver/subscription
plumbing into a public contract. Second, reports: the four reactive signals
(`onUnhealthy`, `onProtocolViolation`, `onSessionInvalidated`, `onUnsolicitedError`) are genuine
members of `ISessionService`'s one interface, not internal plumbing that happens to need a home —
they are the reactive half of the same domain concept `connect`/`disconnect` are the commanded half
of ("owning a connection's lifecycle"), carrying genuinely different, already-well-typed payloads
that don't reduce to a redundant restatement of something else `RequestService` could already see.
`RequestService`, constructed after `SessionService`, calls these directly as a normal
dependency; there is no cycle and no callback involved on this side.

### Composing narrow authority

`ISessionAdmissionService` and `ISessionTrustService` exist specifically because `admitSession` and
`markTrusted` are real privileged capabilities that must never be reachable from a class other than
the one that legitimately performs them — `AuthenticationService` and `PairingService`
respectively, and nothing else. This is enforced by the dependency graph itself: no other consumer
is ever given either interface.

## Session-state ownership

`SessionState` is created exactly once, by the composition root (`DovahLinkClient`), and is the
single authoritative owner of every session-scoped mutable fact this engine has: connection state,
`sessionId`, trust state, the administrative invalidation reason, the connection generation, the
last-connected URI, and the transport's message subscription. Direct `SessionState` access is
limited to the session subsystem's own internal components that legitimately participate in
maintaining it: `SessionService`, `SessionAdmissionService`, `SessionTrustService`, and
`ConnectionTeardownCoordinator` (`SessionService`'s own supporting collaborator, per
"Internal composition", through its explicit contract). The composition root itself also
holds `SessionState` only transiently, to construct it once and pass it to these holders — it never
keeps it as a field. Every other consumer — `IRequestService`,
`IAuthenticationService`, `IPairingService`, `IReconnectService` — depends on the appropriate Service
contract, never on `SessionState` directly. Never mirror or cache a session-scoped mutable fact in
another service merely because it's needed there; the one documented, accepted exception is
`AuthenticationService`'s own cached `clientId`/`bridgeVersion`, which are read-caches of values
whose durable source of truth is `IClientStorage`, refreshed every `hello()` — not a competing copy
of anything `SessionState` owns.

`SessionAdmissionService` exposes the one write command that admits a newly authenticated
session (`admitSession`, called once by `AuthenticationService` after a successful `hello`);
`SessionTrustService` exposes the one write command that upgrades trust standing (`markTrusted`,
called by `PairingService` after a successful pairing acknowledgement). No other class assigns
`sessionId` or trust state directly.

`ReconnectService` never assigns connection state directly either: it only drives the same
`connect`/`disconnect` commands (via `ISessionService`) and the same `hello` call (via
`IAuthenticationService`, an explicit constructor dependency) any other caller uses.
`SessionService` still decides the resulting state transitions itself -- entering `reconnecting`
only after ordinary transport loss tears down cleanly with a known endpoint (driving
`ReconnectService` through the `onOrdinaryTransportLoss` callback above), moving to
`reauthenticating` once a recovery attempt's transport reconnects (trust not yet confirmed), and
resolving out of that to `connected` (that attempt's `hello` actually admits a session) or
`disconnected` (a deliberate disconnect, an administrative invalidation, or the reconnect service's
own final give-up) -- so `ReconnectService` orchestrates *when* to retry while `SessionService`
remains the sole owner of *what state that produces*.
