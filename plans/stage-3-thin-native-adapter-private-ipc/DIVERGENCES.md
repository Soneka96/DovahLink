# Stage 3 divergences

## D1 — Remove private protocol-version negotiation

Original requirement: `ai/context/host/architecture.md`, “Framing and
versioning”: the private channel “carries an explicit version negotiated at
connection start”.

Observed conflict: The maintainer directed that host and adapter are shipped as
one atomic package and should not negotiate a private protocol version. The
Concept 01 implementation had encoded a version byte and HelloAck version
field.

Proposed change: Remove the private frame version byte, the HelloAck negotiated
version field, and the associated rejection values. Use the existing bounded
peer-ownership proof and Skyrim-lifetime/process ownership proof to ensure the
connection belongs to the matching packaged host/adapter pair. Keep malformed,
unauthorized, and over-limit input fail-closed behavior.

Impact: This removes cross-version negotiation and makes mismatched binaries a
deployment/ownership failure rather than a negotiated compatibility case. Host
and adapter remain separate OS processes, and the host-not-ready state remains
necessary during startup, reconnect, and failure recovery.

Status: approved

Decision source: Direct maintainer instruction in the current task on
2026-08-30.

## D2 — Mutual host authentication in the Hello/HelloAck handshake

Original requirement: `01-private-ipc-contract.md` (status: complete), whose
`IpcHelloMessage`/`IpcHelloAckMessage` fields and C#/C++ golden vectors were
recorded as the final no-version frame layout for "all current message
kinds."

Observed conflict: while designing Concept 04's process-lifecycle rendezvous,
the maintainer identified that `IpcHelloAckMessage.accepted = true` alone does
not prove the responder is the legitimate host: it only proves the *adapter's*
`peerProofToken` was checked by whatever process is listening on the
discovered port. Nothing in the existing handshake proves the reverse -- that
the responder actually holds the shared secret, or that its response is fresh
rather than replayed from an earlier exchange. This matters once Concept 04
introduces a discovery mechanism (a rendezvous file/environment value) that
names a port for the adapter to dial, rather than the adapter always being the
one a priori-trusted static endpoint.

Proposed change: extend the handshake to be mutually authenticated:
- `IpcHelloMessage` gains a fresh, adapter-generated random `challenge` (32
  bytes) and an `ownerLifetimeId` (12 bytes: the owning Skyrim process's PID
  and creation timestamp; see `04-process-lifecycle.md`'s "Lifetime-scoped
  rendezvous and shutdown identity").
- `IpcHelloAckMessage` gains a `hostProof`: `HMAC-SHA256` keyed by the shared
  `peerProofToken`, computed over `challenge || correlationId ||
  adapterInstanceId || ownerLifetimeId`.
- The host rejects (new `IpcHelloRejectReason::kLifetimeMismatch`) a Hello
  whose `ownerLifetimeId` does not match the value the host itself was
  launched with, in addition to the existing `peerProofToken` check.
- The adapter's session treats a connection as authenticated only when the
  exact conjunction `accepted && matching correlationId && matching fresh
  challenge && matching ownerLifetimeId && constantTimeEqual(hostProof,
  expectedProof)` holds -- `accepted` and a verifying `hostProof` are both
  required; neither alone is sufficient, and a verifying `hostProof` never
  overrides `accepted = false`. A HelloAck with a missing, forged, wrong, or
  replayed `hostProof`, or any mismatched field in that conjunction, is
  treated as a rejected handshake. The exact byte layout, field order, and a
  shared C++/C# known-answer test vector are recorded in
  `04-process-lifecycle.md`'s "Exact HMAC encoding".
- `ownerLifetimeId` (12 bytes: the owning Skyrim process's PID and creation
  timestamp) is added to `IpcHelloMessage` alongside `challenge`, scoping the
  handshake to the intended Skyrim lifetime; see
  `04-process-lifecycle.md`'s "Lifetime-scoped rendezvous and shutdown
  identity" for why this is a collision-avoidance value, not itself a
  cryptographic ownership proof.

Impact: Concept 01's frame layout, both codecs, and its golden vectors change
(new fields, new reject reason). This is an authentication-depth fix, not a
reversal of D1's "no protocol-version negotiation" stance: no version field is
reintroduced, and same-package ownership/lifetime proof remains the
compatibility boundary -- this divergence only makes that proof actually
mutual and replay-resistant.

Status: approved

Decision source: Direct maintainer instruction in the current task on
2026-08-30.
