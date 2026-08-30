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
