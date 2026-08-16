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

## Subscription intent versus mechanics

A consumer expresses intent ("I want player state"); the SDK owns whether satisfying it currently
requires a new remote subscription, reuse of an existing one, an initial snapshot, reconnect
recovery, resubscription, revision-gap recovery, play-context invalidation, or stale-state
suppression. The app must not maintain a competing protocol-level truth (for example a boolean
tracking whether the server is subscribed); it may know a screen currently wants the state, but the
SDK knows whether the remote subscription and recovery state are actually valid.

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
