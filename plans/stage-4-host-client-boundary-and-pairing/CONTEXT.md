# Current phase context

Source: `host/PLAN.md`
Source fingerprint: `host/PLAN.md` — `7434ECE0A3ACDBF9A7D86460F080D1BC7310B4AF6C2A15BF8868C676DCB1CC0C`
Phase: Stage 4 — Host Client Boundary and Pairing
Package: `plans/stage-4-host-client-boundary-and-pairing/`
Status: pending

The broad local `.git/info/exclude` rule for `PLAN.md` has been removed. `host/PLAN.md` and this
package's `PLAN.md` are now visible to Git as durable planning sources, but the maintainer must
still stage and persist them before a fresh clone can satisfy the handoff gate.

## Cold-start handoff gate

A new AI must have no dependency on this conversation. Before doing any implementation, it must
verify that all of these files are present in the checkout: `AGENTS.md`, `host/PLAN.md`, this
package's `PLAN.md`, this `CONTEXT.md`, `DIVERGENCES.md`, the active concept file, and the required
architecture/security/testing context named by the package plan. If `host/PLAN.md` or the package
`PLAN.md` is absent, ignored-only, or unavailable from the persisted checkout, the AI must stop and
ask the maintainer to persist the planning files; it must not reconstruct them from memory, chat,
source guesses, or the child concept files.

Current persistence status: `host/PLAN.md` and this package's `PLAN.md` are committed to the
repository (commit `049923f`, "docs: persist host migration plans and align Stage 4 contracts").
The cold-start handoff gate is satisfied.

After the files are present, the new AI must verify the source SHA-256, active concept, feature
branch, D1/D2/D3 status, and clean scope before implementation.

## Active concept

- File: `02-authentication-and-session-admission.md`
- Status: not started; awaiting explicit maintainer authorization to begin
- Prerequisites: Concept 01 complete; Stage 2 authentication/trust/identity/session services and Stage 3 host composition available; feature branch `feature/4-host-client-boundary-and-pairing` active; cold-start handoff gate satisfied
- Next action: Revalidate the source fingerprint and this ledger, then implement only Concept 02 once the maintainer names it as the requested scope.

## Completed concepts

- `01-public-websocket-transport.md`: complete, including a post-conventions-cleanup corrective pass.
  Loopback-only dual-stack listener (`127.0.0.1` and `::1`, one shared port; a deterministic test
  proves both sockets bind the explicit loopback address rather than a wildcard, alongside the
  accepted-remote-address check), single-connection admission slot enforced by decoupling accept from
  serve (a serial accept-then-serve loop cannot reject a same-family second connection promptly, since
  it never returns to `AcceptAsync` while serving the first), strict RFC 6455 handshake validation
  under a deadline (requires a non-empty `Host` header and request target, rejects a repeated
  singleton header, and validates the decoded `Sec-WebSocket-Key` is exactly 16 bytes), a bounded
  reader/writer that reassembles fragmented messages up to the byte bound (a positive
  fragmentation-then-dispatch proof exists alongside the oversized-fragment rejection proof) with
  WebSocket-native keep-alive budgeted with headroom below the approved 60-second liveness ceiling
  (`Constants.PublicWebSocketKeepAliveInterval` is a fixed 50 seconds plus a 5-second
  `KeepAlivePongTimeout`, leaving roughly 5 seconds of headroom rather than summing to the ceiling
  exactly, because .NET's managed WebSocket keep-alive scheduler polls on a `Timer` tick of
  `min(interval, timeout) / 4` instead of firing at an exact instant -- an earlier revision's exact
  55s+5s=60s split could itself overshoot the ceiling under normal scheduling jitter; no hand-rolled
  heartbeat), and one deterministic teardown path: a mandatory synchronous `HandleConnectionEnded()`
  lifecycle seam runs before any best-effort `HandleDisconnectedAsync()` cleanup and before the
  connection can complete or its admission slot can be reused, with a bounded
  disconnect-notification timeout and a bounded graceful-close/abort fallback. All steps and
  follow-up fix/corrective passes are independently tested; see "Deferred debt" below for the
  accepted scope simplifications.

## Decisions and approved deviations

- Option A approved by the maintainer on 2026-09-01: defer public host-instance identity, keep the existing envelope field explicitly unavailable for Stage 4, and publish no live state in this phase. See `DIVERGENCES.md` D1.
- The public client contract remains separate from private adapter IPC.
- The public administration surface remains limited to the existing canonical messages and host services; no speculative public list/revoke/block/reset protocol is added.
- The Stage 4 public message matrix in `PLAN.md` is authoritative for implementation allowlists; server-originated messages must not be accepted as client requests.
- D2 is approved: restricted sessions allow the complete pairing allowlist from the canonical schema, including `pairing_ack`, `pairing_renotify`, and `pairing_cancel`; the abbreviated source wording has been reconciled.
- `bridgeVersion` remains required and uses the existing transitional `bridge/vcpkg.json` value without a phase-branch version bump; `bridgeInstanceId` remains the approved D1 limitation.
- D3 is approved: Stage 4's public transport implements the approved 128-message/2 MiB outbound bound as one flat pool rather than the reserved-control/Normal/Heavy lane split, since no live data lane exists yet to compete for capacity. See `DIVERGENCES.md` D3.
- Root `PLAN.md` is marked historical/paused reference, not implementation authority; `ROADMAP.md`, `host/PLAN.md`, and this package are authoritative for Stage 4 work.

## Deferred debt

- Public host-instance identity and any required canonical envelope/protocol revision remain deferred until before live state publication/cutover.
- Live state publication, bounded delivery, and real capture remain Stage 5 and later.
- Pairing availability must be tied to an accepted adapter display/redisplay operation; a challenge must not be reported as available merely because a code was generated in host memory.
- Pairing forwarding includes initial display, manual redisplay, wrong-code automatic redisplay, and the no-code attempts-exhausted notification; none of these values may leak onto the public wire.
- Session records must retain authentication source/trust tier, and administrative invalidation must preserve reason-specific best-effort notification before close without targeting developer-token sessions through a matching self-declared `clientId`.
- Resolved by Concept 01: the public transport enforces the approved bounded outbound queue even though Stage 4 has no live data lane; responses cannot accumulate without bound. See the flat-pool-versus-lane-split nuance recorded further below.
- The frozen bridge remains production and owns port `58231` until the later cutover gate; Stage 4 must not activate replacement packaging beside it.
- Private adapter coverage includes host→adapter pairing notifications and adapter→host Papyrus trust administration (`help`, `list`, `revoke`, `block`, `unblock`, `forget`, `reset-trust`, `reset`, `confirm-reset`) with typed correlation-scoped messages.
- D2 is resolved: the canonical complete restricted pairing allowlist is now reflected in `security.md`, `protocol/schema/README.md`, and `ai/context/host/migration-audit.md`.
- Concept 01's public transport implements the approved 128-message/2 MiB outbound queue bound as
  one flat pool, not yet split into the approved 16-slot reserved-control / 108-slot Normal / 4-slot
  Heavy lane structure, because no live data lane exists yet to compete for capacity against
  connection/control traffic. This split remains deferred until live state publication exists to
  need it; a later phase that adds Normal/Heavy/Snapshot/Event traffic must partition
  `PublicWebSocketTransportOptions.OutboundQueueMaxMessages`/`OutboundQueueMaxBytes` by traffic
  class before publishing state, so a slow client under publication pressure cannot delay or crowd
  out control-message delivery.
- Concept 02 must prove its `IPublicWebSocketMessageHandler.HandleConnectionEnded()` implementation
  invalidates the exact socket's authenticated session, performs only local/in-memory lifecycle work,
  is idempotent-safe, cannot block on network/disk/adapter I/O, and cannot throw under normal
  session-registry conditions. Concept 01's transport tolerates a throwing `HandleConnectionEnded()`
  so a handler bug can never leak the socket, but that same tolerance means an authenticated session
  is not guaranteed removed if Concept 02's implementation throws before completing invalidation;
  Concept 02 owns closing that gap, not a further Concept 01 change. Best-effort secondary cleanup
  belongs in `HandleDisconnectedAsync()` instead.

## Changed files

- `host/PLAN.md`: documented the parallel relationship to the product roadmap.
- `ROADMAP.md`: documented the parallel host/adapter migration track and its cutover gate.
- `ai/context/host/migration-audit.md`, `ai/context/protocol/security.md`, and
  `protocol/schema/README.md`: reconciled the approved D2 restricted-session allowlist.
- `plans/stage-4-host-client-boundary-and-pairing/`: phase planning package and ledger updates.
- `.git/info/exclude`: removed the broad local `PLAN.md` exclusion.
- Concept 01, new: `host/DovahLink.Host/Client/Transport/PublicWebSocketTransportOptions.cs`,
  `IPublicWebSocketMessageHandler.cs`, `PublicWebSocketHandshake.cs`, `PublicWebSocketConnection.cs`,
  `PublicWebSocketListener.cs`; matching new test files under
  `host/DovahLink.Host.Tests/Client/Transport/`; new test doubles
  `FakePublicWebSocketMessageHandler.cs`, `FailAfterFirstWriteStream.cs`,
  `FakePublicWebSocketConnection.cs`, `ThrowingPublicWebSocketConnection.cs`; and the test project's
  first `Fixtures.cs` builder.
- Concept 01, modified: `host/DovahLink.Host/Constants.cs` (public-transport bounds).

## Verification

- `git branch --show-current`: `feature/4-host-client-boundary-and-pairing`
- `host/DovahLink.Host.Tests`: 528 passed before Concept 01; 592 passed after Concept 01's original
  three steps and follow-up fix pass; 622 passed after the post-conventions-cleanup corrective pass
  (60-second liveness deadline fix, deterministic loopback-bind proof, plus the RFC 6455 and
  fragmented-message hardening and the mandatory `HandleConnectionEnded()` seam already present
  going into this pass); 624 passed after this liveness-precision corrective pass (the exact-sum
  claim replaced with a budgeted-with-headroom one, plus two tests pinning the approved 50s/5s split)
- `integration/DovahLinkValidationClient.Tests`: 357 passed, re-run during this corrective pass; unchanged
- `ctest --test-dir adapter/build/windows-x64-debug --output-on-failure`: 312 passed, re-run during this corrective pass; unchanged
- `python -m unittest discover -s tooling -p "test_*.py"`: 93 passed
- `dotnet build ... -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=true`: clean
- `host/PLAN.md` SHA-256: `7434ECE0A3ACDBF9A7D86460F080D1BC7310B4AF6C2A15BF8868C676DCB1CC0C`

## Handoff

Next concept: `02-authentication-and-session-admission.md`
Blocked by: explicit maintainer authorization naming Concept 02 as the requested scope. Per the
package's own execution guardrails, implementation does not auto-proceed from one concept to the
next; the maintainer must confirm before Concept 02 begins.
