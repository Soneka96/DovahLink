# Stage 5A — Android and Secure Wi-Fi Development Path

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./05-dart-client-sdk-foundation.md) · [Next stage](./06-pc-second-screen-baseline.md)

## 5A. Android and Secure Wi-Fi Development Path

**Status:** Planned

This is an intentionally narrow delivery slice pulled forward from the later LAN and mobile stages
so the official Flutter client can be developed and validated on a real Android device. It does not
close Stage 5, Stage 22, or Stage 23.

### Outcome

The maintainer can install the official Flutter client on an Android phone, open it while the phone
and PC are on the same ordinary local Wi-Fi network, discover available DovahLink Bridges, select
one, pair when required, and connect to it without manually rebuilding or editing a fixed Bridge
port into the app.

### Scope and behavior

- Add Android as a supported development target for the official Flutter client.
- Add the Android implementation of the SDK's `IClientStorage` platform port using an approved
  Android-appropriate secure-storage primitive. The Android adapter owns the same SDK state as the
  Windows adapter: `clientId`, trusted-device credential, and pairing recovery state.
- Add SDK-owned local Bridge discovery using an established local service-discovery mechanism,
  initially DNS-SD/mDNS unless an implementation constraint requires another approved mechanism.
  The SDK performs discovery, timeout, deduplication, stale-candidate expiry, and candidate
  normalization; Flutter receives typed candidates rather than discovery packets.
- Have each Bridge publish a small non-secret discovery record containing enough information to
  display a candidate and begin connection establishment. Discovery metadata is untrusted until
  the endpoint proves its Bridge identity through the authenticated connection flow.
- Replace the fixed runtime port assumption with automatic OS-selected port allocation by default.
  The Bridge publishes the actual bound endpoint in its discovery record. An explicit port remains
  available for deterministic tests, diagnostics, and a temporary compatibility path; the old
  `58231` value is not a Bridge identity and clients must not depend on it once discovery is active.
- Preserve the existing user flow: entering the app shows discovered Bridge candidates, selecting a
  candidate carries that candidate into pairing, and pairing/authentication connects to the selected
  endpoint. The app retains manual endpoint entry as a recovery fallback.
- Use the approved LAN security design before accepting a non-loopback client. Discovery is not
  authentication: the implementation must authenticate the intended Bridge, use established
  authenticated encryption, preserve session binding and replay protection, and keep pairing,
  authorization, revocation, and rate limits intact. A developer token must never be sent to a
  non-loopback peer.
- Keep this slice limited to one active client connection, foreground Android use, one normal local
  Wi-Fi network, and the existing read-only companion workflow. The phone replaces the desktop
  client during development; simultaneous desktop and phone connections are outside this slice.

### Required boundary decisions

- The LAN threat model and authenticated-transport choice must be approved and recorded in the
  protocol security context before implementation. This stage must not introduce an insecure
  development-only LAN bypass that could become a product path.
- Bridge identity remains independent of hostname, IP address, port, discovery service name, or
  display name. A discovered endpoint is a connection candidate, not durable identity.
- The discovery record is deliberately non-secret. A matching service name or search query reduces
  noise but does not prove ownership of a Bridge and is never used as a credential.
- The actual endpoint selected by the OS is authoritative for that Bridge instance. The SDK and app
  must not recreate an endpoint from a hardcoded port after discovery.

### Dependencies and boundaries

This slice consumes the identity and pairing semantics from Stages 2 and 3 and the pulled-forward
SDK transport and persistence boundaries from Stage 5. It is an early, single-client slice of the
LAN and mobile work later generalized by Stages 9–11, 22, and 23. It does not change the ownership
of protocol schemas, Bridge authority, SDK client behavior, or Flutter presentation boundaries.

The first supported network is one ordinary Wi-Fi LAN where the phone and PC can reach each other
directly. Guest-network client isolation, VPNs, mobile hotspots, routed or multi-subnet discovery,
IPv6-only environments, firewall edge cases, and other network-topology compatibility concerns are
deferred to later hardening.

### Explicit non-goals

- No internet, hosted relay, account system, or cloud synchronization.
- No background execution, push notifications, or full network-transition recovery while the app is
  suspended.
- No iOS or tablet-specific presentation work.
- No simultaneous desktop and phone clients; multi-client delivery remains Stage 9.
- No automatic Bridge selection or resident monitor; candidate selection remains explicit and the
  broader connection policy remains Stage 11.
- No unauthenticated LAN mode, shared developer-token mode, or security exception for local Wi-Fi.

### Acceptance criteria

- An Android debug build installs and launches on the supported development phone, and the SDK uses
  the Android storage adapter without importing or executing the Windows DPAPI implementation.
- Two Bridge instances can start without manual port editing, bind distinct automatically selected
  ports, and publish their actual endpoints without treating those ports as identity.
- SDK discovery returns all valid same-LAN Bridge candidates, removes stale or expired candidates,
  deduplicates repeated advertisements, and ignores malformed or non-DovahLink records.
- Discovery records contain no credential or developer token, and a spoofed or mismatched candidate
  cannot become a trusted Bridge merely by matching the service name or search query.
- The app displays the discovered candidates, preserves the selected candidate through navigation,
  and authenticates against that candidate's endpoint rather than the old static default URI.
- A first-time phone connection completes the approved pairing flow; a later foreground reconnect
  uses the Android-persisted trusted-device credential without repeating pairing.
- The existing one-client constraint is respected: the phone can replace the desktop client, but a
  second simultaneous client is rejected or handled according to the current Bridge contract.
- Manual endpoint fallback remains available when discovery is unavailable, and the UI explains
  that the phone and PC must be reachable on the same ordinary local network.
- Automated tests cover port allocation and explicit-port injection, discovery decoding and stale
  records, candidate/identity separation, selected-endpoint propagation, Android storage behavior,
  and failure paths for unavailable, spoofed, malformed, or unreachable candidates.
- A real-device smoke test proves the complete foreground flow on one ordinary Wi-Fi network:
  discover, select, pair, connect, disconnect, restart the app, and reconnect.

### Deferred follow-up

Stage 22 generalizes and hardens the secure LAN transport and discovery surface across supported
clients and network environments, and feeds its candidates into Stage 11's connection policy.
Stage 23 completes mobile/tablet presentation, background/resume behavior, network transitions, and
mobile-specific recovery. Network-environment compatibility is tracked as a separate hardening
concern rather than making this first development path depend on every possible Wi-Fi topology.
