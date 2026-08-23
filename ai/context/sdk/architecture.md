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

The client engine described above is implemented as a small set of internally composed,
individually-testable classes rather than one class performing every mechanic. `ClientSession` owns
transport lifecycle, connection state, and stream ownership; `MessageRouter` owns envelope decoding,
correlation, and unsolicited routing; `RequestManager` owns pending requests, timeout policy, and
retry behavior; `PendingOperationTransmitter` owns each operation's message-ID generation,
registration, timeout arming, envelope construction, and transport error reporting;
`AuthenticationService` owns `hello`/authentication and credential recovery; `PairingService` owns
pairing operations; `ConnectionTeardownCoordinator` owns ordered transport-resource cleanup;
`PendingOperation` is the internal record of one request awaiting its reply. Each is a named class
in its own file under `src/`, absent from the public
barrel per `ai/context/sdk/api-design.md`'s "curated public exports" — a façade/coordinator class
delegates to these instead of implementing their mechanics itself, per
`ai/context/dart/dart-style.md`'s class-organization rule.

These classes cross their composition boundaries through small named ports (`abstract interface
class`, matching `DovahLinkTransport`/`ClientStorage`'s existing style) rather than callback
closures or a concrete back-reference to a specific class:

- `SessionContext` — read-only access to the current `sessionId`/trust state, for a collaborator
  that needs to know the live session without commanding it.
- `ConnectionLifecycleReporter` — reports an unhealthy connection, a protocol violation, an
  unsolicited bridge-reported error, or an administrative session invalidation upward to whichever
  class owns the connection.
- `SessionConnector` — connects, disconnects, reads connection state, and admits a newly
  authenticated session. Extends `SessionContext`, since every current `SessionConnector` consumer
  also needs `SessionContext`'s read access; see the inheritance note below.
- `SessionTrustWriter` — upgrades trust standing alone, for a collaborator that never connects or
  disconnects.
- `MessageReceiver` — ensures the transport's inbound message stream is being read, for a
  collaborator that sends a request before the connection is otherwise guaranteed to be receiving.
- `RequestSender` — sends one typed request and awaits its correlated reply, for a collaborator
  that performs a protocol operation without owning pending-operation state.
- `ReplyResolver` — resolves one inbound correlated reply, for the router that matches transport
  messages without owning pending-operation state.
- `PendingOperationRegistry` — registers a generated outgoing `messageId` with the class that owns
  pending-operation state before the corresponding wire attempt is sent.
- `SessionLifecycleState` — exposes only the state commands a teardown coordinator needs, while the
  session class remains the sole owner of connection state, generation, and stream fields.
- `PendingOperationFailureHandler` — fails or orphans pending operations during connection teardown
  without exposing the request manager's pending-operation storage.

These ten ports are independent, not one merged interface, and not related by inheritance by
default: a collaborator composes only the ports its own responsibility actually needs (for example
`PairingService` takes `RequestSender`, `SessionTrustWriter`, and `MessageReceiver`, and
`MessageRouter` takes `ReplyResolver` and `ConnectionLifecycleReporter`, while
`PendingOperationTransmitter` takes `PendingOperationRegistry` and `ConnectionTeardownCoordinator`
takes `SessionLifecycleState` and `PendingOperationFailureHandler`), keeping each dependency as
narrow as the class that declares it. One narrow, justified exception exists: `SessionConnector`
extends `SessionContext` because every real consumer that needs `SessionConnector`'s
connect/disconnect/admit commands also needs `SessionContext`'s read access, while a consumer that
needs only the read side (for example `RequestManager`/`PendingOperationTransmitter`, stamping the
live `sessionId` onto an outgoing envelope) keeps depending on bare `SessionContext` alone --
inheritance here removes a redundant constructor parameter from the one consumer that always needed
both, without widening any narrower consumer's access. This is not a general merge policy: a new
port still defaults to standing alone unless the same real-world "every consumer of the wider port
already needs the full narrower port too" pattern applies.

## Session-state ownership

`ClientSession` owns every socket-scoped field this engine has: connection state, `sessionId`,
trust state, the administrative invalidation reason, the connection generation, and the transport
subscription. No other class — including the façade — assigns `sessionId` or trust state directly;
`ConnectionTeardownCoordinator` changes connection-scoped state only through
`SessionLifecycleState`, and never owns a second copy of that state. `ClientSession` exposes exactly
two write commands for the transitions the rest of the engine needs
to trigger: `admitSession` (a newly authenticated session, called once by `AuthenticationService`
after a successful `hello`) and `markTrusted` (a trust upgrade, called by `PairingService` after a
successful pairing acknowledgement). `admitSession` itself triggers `RequestManager`'s
retry-orphaned-operations transition as part of admitting the session, keeping reconnect/session
recovery cohesive in one place rather than split across the class that authenticates and the class
that owns the session.
