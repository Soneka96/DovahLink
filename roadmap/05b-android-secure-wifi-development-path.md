# Stage 5B — Android and Secure Wi-Fi Development Path

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./05a-windows-sas-integration-validation.md) · [Next stage](./06-pc-second-screen-baseline.md)

## 5B. Android and Secure Wi-Fi Development Path

**Status:** Planned

This stage was originally specified as Stage 5A. The 5A.0 rebaseline split that stage: Windows SAS
pairing integration validation moved to
[Stage 5A — Windows SAS Integration Validation](./05a-windows-sas-integration-validation.md), and the
Android and secure Wi-Fi requirements below are retained here unchanged in meaning. Completing Stage
5A does not approve or unblock this stage; it remains blocked wherever the production-security
approvals it needs are missing.

This stage is gated on completion of security migration S1–S11. Its target identity, pairing, and
transport authority is [`ai/context/security/identity-and-transport.md`](../ai/context/security/identity-and-transport.md);
the current runtime rules remain in `ai/context/protocol/security.md` until cutover.

This is an intentionally narrow delivery slice pulled forward from the later LAN and mobile stages
so the official Flutter client can be developed and validated on a real Android device. It does not
close Stage 5, Stage 22, or Stage 23.

### Outcome

The maintainer can install the official Flutter client on an Android phone, open it while the phone
and PC are on the same ordinary local Wi-Fi network, discover available DovahLink Hosts, select
one, pair when required, and connect to it without manually rebuilding or editing a fixed Host
port into the app.

### Scope and behavior

- Add Android as a supported development target for the official Flutter client.
- Keep the Stage 5B Android client landscape-only and reuse the same primary second-screen layout
  structure/model as the Windows client. This shared layout must still respond to the phone's smaller
  physical viewport through appropriate sizing, scrolling, and existing responsive minimum
  constraints; reusing the layout model does not mean using fixed desktop pixel dimensions.
- Add the Android implementation of the SDK's `IClientStorage` platform port using an approved
  Android-appropriate secure-storage primitive. The Android adapter owns the same SDK security
  state as the Windows adapter: `clientId`, Client cryptographic identity/key reference, KnownHosts
  and Host pins, Host-scoped pairing recovery, and related SDK security persistence.
- Add SDK-owned local Host discovery using an established local service-discovery mechanism,
  initially DNS-SD/mDNS unless an implementation constraint requires another approved mechanism.
  The SDK performs discovery, timeout, deduplication, stale-candidate expiry, and candidate
  normalization; Flutter receives typed candidates rather than discovery packets.
- Have each Host publish a small non-secret discovery record containing enough information to
  display a candidate and begin connection establishment. Discovery metadata is untrusted until
  the endpoint proves its Host identity through the authenticated connection flow.
- Replace the fixed runtime port assumption with automatic OS-selected port allocation by default.
  The Host publishes the actual bound endpoint in its discovery record. An explicit port remains
  available for deterministic tests, diagnostics, and a temporary compatibility path; the old
  `58231` value is not a Host identity and clients must not depend on it once discovery is active.
- Preserve the existing user flow: entering the app shows discovered Host candidates, selecting a
  candidate carries that candidate into pairing, and pairing/authentication connects to the selected
  endpoint. The app retains manual endpoint entry as a recovery fallback, and that fallback follows
  the same Host-authentication-before-client-pairing sequence as discovered candidates.
- Use the approved security design before accepting a non-loopback client. Discovery is not
  authentication: the implementation must authenticate the intended Host, use established
  authenticated encryption, preserve session binding and replay protection, and keep pairing,
  authorization, revocation, and rate limits intact. Follow
  [`ai/context/security/identity-and-transport.md`](../ai/context/security/identity-and-transport.md)
  for the target identity and first-pair bootstrap. A developer token must never be sent to a
  non-loopback peer.
- Keep this slice limited to one active client connection, foreground Android use, one normal local
  Wi-Fi network, and the existing read-only companion workflow. The phone replaces the desktop
  client during development; simultaneous desktop and phone connections are outside this slice.

### Required boundary decisions

- Before implementing or enabling any non-loopback Host listener, complete the S1–S11 security
  migration. The target transport and first-pair bootstrap are governed by
  `ai/context/security/identity-and-transport.md`; current exposure/authentication rules remain in
  `ai/context/protocol/security.md` until the approved cutover. Stage 22 later generalizes LAN
  hardening and discovery. This stage must not introduce an insecure development-only LAN bypass
  that could become a product path.
- An mDNS/DNS-SD candidate must never become trusted merely because its
  discovery metadata, service name, hostname, or endpoint matches the requested search. An approved
  initial-pairing profile must bind the intended Host and Client keys before either side
  commits trust. S2.2 selected no production profile; the endpoint and its advertised metadata remain
  untrusted until a profile passes the security gate.
- The required conceptual connection sequence is:

  ```text
  untrusted discovery candidate
      -> connect to candidate endpoint
      -> perform an approved initial-pairing binding, or verify the pinned Host key
      -> prove Client-key possession and receive typed Host trust state
      -> persist the established KnownHost/key binding and applicable recovery metadata
  ```

  This roadmap slice does not select an initial-pairing algorithm or library. S2.2 ended STOP without
  selecting a production profile. Its security boundary remains: unknown Host identity must not be
  trusted before an approved initial-pairing profile passes the security gate.
- Host identity remains independent of hostname, IP address, port, discovery service name, or
  display name. A discovered endpoint is a connection candidate, not durable identity.
- The discovery record is deliberately non-secret. A matching service name or search query reduces
  noise but does not prove ownership of a Host and is never used as a credential.
- The actual endpoint selected by the OS is authoritative for reaching that candidate at that
  time; it remains routing information, not Host identity. The SDK and app must not recreate an
  endpoint from a hardcoded port after discovery.

### Dependencies and boundaries

This slice consumes the identity and pairing semantics from Stages 2 and 3 and the pulled-forward
SDK transport and persistence boundaries from Stage 5. It is an early, single-client slice of the
LAN and mobile work later generalized by Stages 9–11, 22, and 23. It does not change the ownership
of protocol schemas, Host authority, SDK client behavior, or Flutter presentation boundaries.

The first supported network is one ordinary Wi-Fi LAN where the phone and PC can reach each other
directly. Guest-network client isolation, VPNs, mobile hotspots, routed or multi-subnet discovery,
IPv6-only environments, firewall edge cases, and other network-topology compatibility concerns are
deferred to later hardening.

### Relationship to Stage 5A and `sas-pairing`

Activating the dormant Host `sas-pairing` foundation (Host identity key and integration composition,
the Host listener and driver lifecycle, the interactive Skyrim SAS prompt, Flutter SAS rendering,
the human MATCH/MISMATCH decisions, and handing a local result to DovahLink's pairing authorization)
is owned by [Stage 5A](./05a-windows-sas-integration-validation.md), restricted to the approved
Windows loopback development environment under the maintainer's pre-alpha P10 authorization (see the
[initial-pairing security deviation](deviations/initial-pairing-security/README.md#p10-pre-alpha-integration-authorization)).

This stage consumes what Stage 5A validates but inherits no security approval from it:

- Stage 5A validation is loopback integration evidence on Windows. It is not evidence of
  hostile-network security, and it does not complete S3–S11 or close the S2.2 STOP.
- `sas-pairing` is experimental and pre-alpha, and does not support Android (its own P11 milestone).
  Android initial pairing needs an Android-capable carrier and Client key, and its own approval.
- Android Keystore Client identity, Host pinning, WSS/TLS, and Client proof of possession remain this
  stage's work under S4–S7 and are not delivered by Stage 5A.
- The existing six-digit flow remains the active product behavior outside Stage 5A's validated
  Windows path, and is not production security for hostile-network first contact.

### Explicit non-goals

- No internet, hosted relay, account system, or cloud synchronization.
- No background execution, push notifications, or full network-transition recovery while the app is
  suspended.
- No portrait-specific Android layout or broader adaptive mobile/tablet presentation; those remain
  Stage 23 work.
- No iOS or tablet-specific presentation work.
- No simultaneous desktop and phone clients; multi-client delivery remains Stage 9.
- No automatic Host selection or resident monitor; candidate selection remains explicit and the
  broader connection policy remains Stage 11.
- No unauthenticated LAN mode, shared developer-token mode, or security exception for local Wi-Fi.

### Acceptance criteria

- An Android debug build installs and launches on the supported development phone, and the SDK uses
  the Android storage adapter without importing or executing the Windows DPAPI implementation.
- The Android build opens and returns to the app in the supported landscape orientation, never
  selecting a portrait-specific UI path. It reuses the Windows client's primary second-screen layout
  model on the supported phone's landscape viewport without clipping or unreachable controls, using
  responsive sizing, scrolling, and minimum constraints where required.
- Two Host instances can start without manual port editing, bind distinct automatically selected
  ports, and publish their actual endpoints without treating those ports as identity.
- SDK discovery returns all valid same-LAN Host candidates, removes stale or expired candidates,
  deduplicates repeated advertisements, and ignores malformed or non-DovahLink records.
- Discovery records contain no credential or developer token, and a spoofed or mismatched candidate
  cannot become a trusted Host merely by matching the service name or search query.
- Before any pairing or trust persistence, the selected endpoint must complete an approved
  first-contact bootstrap or verify the pinned Host key. No production bootstrap is currently
  selected. A spoofed, mismatched, or
  unauthenticated endpoint aborts before the client accepts its metadata or persists a KnownHost;
  manual endpoint entry follows the same rule.
- The non-loopback listener remains disabled until the approved LAN threat model, authenticated
  transport, and first-contact bootstrap are ready under S1–S11, and runtime tests prove that
  unauthenticated peers are rejected. Provisional TLS without an approved bootstrap must not expose
  an unknown-Host accept-and-pair path.
- The app displays the discovered candidates, preserves the selected candidate through navigation,
  and authenticates against that candidate's endpoint rather than the old static default URI.
- Android storage retains the Client key reference and per-Host pins/recovery securely; it does not
  persist Host-authoritative `trusted`, `revoked`, or `blocked` state.
- A first-time phone connection completes an approved initial-pairing flow. A later
  foreground reconnect verifies the pinned Host identity, proves Client private-key possession, and
  receives the Host's typed trust result without a bearer credential.
- The existing one-client constraint is respected: the phone can replace the desktop client, but a
  second simultaneous client is rejected or handled according to the current Host contract.
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
