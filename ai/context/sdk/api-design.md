# SDK API design

Public-API conventions for the Dart Client SDK. Read `ai/context/sdk/architecture.md` first for the
one-engine/multiple-views rule this API surface sits on top of.

## Simple-first API

The common developer experience hides boring reusable connection mechanics: raw WebSockets,
Ping/Pong, heartbeat implementation, ports once discovery/selection own them, credentials, secure
storage, session teardown, retry/backoff, revision recovery, snapshot reconciliation, stale-session
suppression, subscription recovery, and Bridge compatibility mechanics. The long-term simple
experience trends toward: find/select a Bridge, pair if necessary, listen to typed state. Only
expose behavior the roadmap phases completed at the time actually support — do not pull discovery,
multi-Bridge, or automatic-connection behavior forward merely to satisfy this shape early; extend
the simple API when those phases land instead.

## Expert capabilities

Advanced developers may inspect lifecycle and diagnostic information: connection state, connected
Bridge version, SDK/Bridge compatibility result, current `bridgeInstanceId`/`playContextId`/
`sessionId`, capabilities, revision/recovery diagnostics, subscription diagnostics, structured
connection/recovery events, and supported administration operations. "Advanced" must not mean
"bypass invariants": an expert API still preserves contract validation, session safety, lifecycle
correctness, security rules, state ownership, and compatibility rules. Do not expose the raw socket
merely because an expert API exists, unless a later explicit low-level API decision approves it.

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
example: pairing code expired, client/device revoked, Bridge unavailable, incompatible Bridge
version, connection lost, recovery failed, state unavailable) rather than freezing exact type names
speculatively. The SDK owns typed meaning; the app owns user-facing wording and presentation. The
app must not parse raw socket exceptions or diagnostic strings to determine product behavior, and
the SDK must not return product-specific UI strings. For incompatibility, the SDK provides enough
structured information for the app to distinguish "Bridge is older than supported" from "Bridge is
newer than supported" when that is safely knowable, per
`ai/context/protocol/compatibility.md`.

Administrative session invalidation is exposed as typed semantic information for SDK consumers:
`revoked`, `blocked`, `trustReset`, and `factoryReset`. These reasons are not durable authoritative
trust state and must not leak as raw string comparisons throughout consumers. The official Flutter
app may map all four to one generic unavailable/disconnected presentation, while third-party SDK
consumers remain free to inspect or display the precise reason.

## Subscription intent versus mechanics

A consumer expresses intent ("I want player state"); the SDK owns whether satisfying it currently
requires a new remote subscription, reuse of an existing one, an initial snapshot, reconnect
recovery, resubscription, revision-gap recovery, play-context invalidation, or stale-state
suppression. The app must not maintain a competing protocol-level truth (for example a boolean
tracking whether the server is subscribed); it may know a screen currently wants the state, but the
SDK knows whether the remote subscription and recovery state are actually valid.

An SDK consumer subscribes and unsubscribes explicitly per domain; subscription must never be
inferred from whether a Dart Stream happens to have listeners. `unsubscribe` for a domain tells the
Bridge to stop traffic for that domain/client rather than only detaching the local listener. After
ordinary reconnect, the SDK restores previously desired subscriptions automatically. After
administrative invalidation, desired subscriptions remain remembered but stay dormant — the SDK
does not reactivate them until an explicit user-initiated Retry succeeds, mirroring the
credential/reconnect policy in `roadmap/03`'s Phase 3.3.

## Reusable models versus presentation models

Typed models representing reusable DovahLink client/domain concepts belong to the SDK; the app maps
SDK outputs into Redux state, ViewModels, presentation/status models, or localized user-facing
representations. Third-party SDK users must not need to depend on official-app domain entities,
Redux, Flutter ViewModels, or product UI concepts, and official-app model architecture must not
accidentally become the SDK's public API.

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
  cannot know whether the Bridge already executed it. This is a transport-level property, unrelated
  to any future Bridge-side command idempotency/replay-protection design.
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
