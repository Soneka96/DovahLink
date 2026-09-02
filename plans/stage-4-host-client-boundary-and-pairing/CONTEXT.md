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
- A pre-merge fresh-eyes audit found Concept 01's own completion criterion -- "the next concept
  receives a transport context with explicit reader/writer ownership and teardown hooks" -- was not
  actually met: `HandleMessageAsync` gave the handler no way to respond on, or close, the exact
  connection that delivered a message, and the only reachable alternative was
  `PublicWebSocketListener.CurrentConnection`, a global/listener-owned lookup unsafe across
  reconnects. A corrective pass added `IPublicConnectionContext`/`PublicConnectionContext`
  (`host/DovahLink.Host/Client/Transport/PublicConnectionContext.cs`, a stateless forwarding adapter
  per `ai/context/common.md`'s "focused capability class" rule rather than a second contract on
  `PublicWebSocketConnection`), now passed as `HandleMessageAsync`'s first parameter and scoped to
  the exact connection instance that constructs it for its entire lifetime, so a stale context from
  an ended connection cannot resolve a later one. It also added
  `IPublicWebSocketConnection.RequestClose()`, an orderly close distinct from the forced close an
  unadmittable outbound message already triggers: cancelling the read loop's own token immediately
  proved destructive during the fix -- .NET's managed `WebSocket` leaves the whole object unusable
  once any pending operation on it is cancelled, which would abandon an already-admitted but
  not-yet-sent frame -- so `RequestClose` completes the outbound queue and defers that cancellation
  until the queue has had a bounded (`GracefulCloseTimeout`) opportunity to drain. All three concept
  files stayed within Concept 01's existing allowlist; no divergence was required.
- Three further fresh-eyes findings against the transport-context corrective pass above were each
  fixed in their own focused pass, all within Concept 01's existing allowlist:
  - A `TrySend` rejected only because `RequestClose` had already completed the outbound queue was
    misclassified as a genuine queue overflow and force-cancelled the writer, destroying an
    already-admitted terminal frame's drain window. The same investigation found the drain wait
    itself was bounded on the wrong signal (`outbound.Reader.Completion`, which fires the instant a
    frame is dequeued, not once its send actually finishes) -- too early for a slow or blocked
    writer, silently discarding the frame regardless of the first fix. `RequestClose` now marks an
    `orderlyCloseInProgress` flag (checked by the forced-close helper) and waits on the writer
    loop's own task (promoted from a `RunAsync`-local variable to a field) rather than the queue
    merely emptying.
  - `orderlyCloseInProgress` was a plain `bool` read and written from threads `TrySend`/`RequestClose`
    both explicitly support running on concurrently; it now goes through `Volatile.Read`/`Write` so a
    racing `TrySend` cannot observe a stale `false` and resurrect the just-fixed force-abort race.
  - `ReadLoopAsync` awaited `HandleMessageAsync` with no bound of its own, so a handler whose
    returned `Task` never completes and ignores cancellation could hold `RunAsync` -- and so the
    listener's admission slot -- open indefinitely during shutdown. The wait is now bounded by the
    same cancellation token already flowing through the read loop (`Task.WaitAsync(cancellationToken)`)
    rather than a new configurable timeout, reusing `RunAsync`'s existing internal-vs-external
    cancellation split unchanged. A follow-up review correctly noted this only bounds a handler once
    it has actually returned that `Task`: it cannot protect against `HandleMessageAsync` itself
    blocking the calling thread synchronously before ever returning one. That boundary is now an
    explicit documented contract requirement on `IPublicWebSocketMessageHandler.HandleMessageAsync`
    instead -- no code can bound a call that has not yet returned control, and introducing one would
    mean a `Task.Run`-based redispatch this pass deliberately does not add. No runtime contract test
    for that specific requirement exists: doing so would need to actually block a thread inside a
    unit test (or the excluded `Task.Run` isolation), and this codebase has no existing precedent for
    runtime-testing the negative case of a "must not block" contract (`HandleConnectionEnded()`'s own
    "must never block on I/O" requirement has none either) -- documentation is the fix here.
- A CI-only failure of `RunAsync_DisconnectNotificationHangs_StillTearsDownWithinBound` (never
  reproduced locally across multiple runs, but confirmed real from the Host workflow's own captured
  test output on `feature/4-host-client-boundary-and-pairing`) traced to a genuine race in
  `NotifyDisconnectedAsync`: the `notifyDeadline` `CancellationTokenSource`'s own internal timeout
  timer and the separate `WaitAsync(options.DisconnectNotificationTimeout)` bounding the outer await
  are two independent timers sharing one nominal duration, not one shared deadline. If `WaitAsync`
  gave up first, under scheduling pressure, the method returned and its `using` block disposed
  `notifyDeadline` before its own pending timer callback had run -- silently discarding that
  callback and leaving the token never actually cancelled, so a hung handler cooperatively awaiting
  it could never unwind. Fixed by explicitly cancelling `notifyDeadline` in a `finally` block
  (idempotent if its own timer already fired), guaranteeing the token is always cancelled before the
  method gives up on it regardless of which timer wins. Verified with 15 repeated runs of the
  regression test (previously 5s-then-fail, now ~260-400ms) and 3 repeated full-suite runs (644/644),
  all with no flake.
- A further fresh-eyes finding against the outbound bound itself: `TrySend`'s message-count check
  relied solely on the bounded `Channel<byte[]>` rejecting `TryWrite` once full, but a `Channel`'s
  capacity frees the instant its reader dequeues an item -- before that item's send over the wire has
  actually finished. With `OutboundQueueMaxMessages` configured at 1, this let a second `TrySend`
  succeed while the first frame was still blocked in-flight inside the writer's own `SendAsync` call,
  so the connection could own one more outbound message than configured (129 at the approved
  128-message bound). The existing outbound byte accounting (`outboundQueuedBytes`) already avoided
  this: it releases only in the writer loop's own `finally`, once a send has actually finished,
  failed, or been cancelled, not merely once the frame left the channel. The message count now
  mirrors that exactly: a new `outboundOutstandingMessages` counter is reserved by `TrySend` alongside
  the byte reservation (with symmetric rollback on either reservation failing, or on a failed
  `Channel.Writer.TryWrite`), and released only in the writer loop's `finally`. Both reservations use
  the same lock-free `Interlocked` reserve-then-rollback idiom the byte accounting already used, so
  `TrySend` remains non-blocking and safe under concurrent callers. Fully within Concept 01's existing
  allowlist; no Stage 5 traffic-lane work was introduced.
- A pre-upgrade allocation-lifetime finding: the constructor unconditionally allocated the 1 MiB
  `messageBuffer` (`Constants.PublicWebSocketMaxMessageBytes`, onto the .NET Large Object Heap) and
  the 4 KiB `receiveChunk` before the WebSocket upgrade was even attempted, so a connection that
  never completed the handshake, sent malformed HTTP, timed out, or disconnected immediately still
  paid the full allocation. Fixed by moving both to locals allocated in `RunAsync`'s
  `if (webSocket is not null)` branch, passed into `ReadLoopAsync` as parameters instead of fields,
  so the allocation is now structurally reachable only after a real WebSocket upgrade succeeded. No
  size limit, handshake behavior, or other transport behavior changed; the existing handshake-timeout,
  malformed/oversized-handshake, and message-bound tests cover this unchanged, re-verified at
  644/644 passed with no new tests required.
- A fragment-trickling resource-exhaustion finding: the inbound message-rate limit
  (`MaxInboundMessagesPerSecond`) only ever ran once a message reached `EndOfMessage == true`, so a
  peer could hold the single admission slot indefinitely by sending one incomplete message a tiny
  fragment at a time, never charged against the rate limit and never time-bounded (the 1 MiB
  `MaxMessageBytes` bound only caps total bytes, not assembly time). Maintainer approved a 5-second
  fragment-assembly deadline, recorded in `ai/context/protocol/security.md`'s "Input limits" as a
  bound distinct from the completed-message rate limit and the idle/liveness timeout. Implemented as
  `Constants.PublicWebSocketFragmentAssemblyTimeout` /
  `PublicWebSocketTransportOptions.FragmentAssemblyTimeout`, enforced entirely inside
  `ReadLoopAsync`: a `CancellationTokenSource` created at most once per message, on its first
  non-final fragment, never recreated or `CancelAfter`'d again by later fragments of that message
  (anchoring it to the first fragment is what closes the trickle gap -- a per-fragment reset would
  only move it), disposed the instant the message completes (before dispatch, so a deadline that
  was about to fire can never be mistaken for a reason to close an already-fully-received message)
  or, for every other exit path, in the read loop's own `finally`. Deliberately does not reuse the
  two-independent-timers shape the `NotifyDisconnectedAsync` fix above had to close: only the
  per-iteration linked-token *wrapper* (needed because `WebSocket.ReceiveAsync` takes one token) is
  recreated per fragment, never the deadline's own single timer. `MaxInboundMessagesPerSecond` still
  counts only completed messages (proven by a new test showing one two-fragment message consumes
  exactly one rate-limit slot), `MaxMessageBytes` is unchanged, and WebSocket-level liveness remains
  a separate, untouched concern. Six new focused tests plus reliance on existing
  unfragmented-message and oversized-fragment tests as regression; 650/650 passed (5 in-scope tests
  from the plan plus 1 fresh-eyes addition covering external cancellation mid-assembly), the new
  fragment-timing tests re-run without flake.

## Decisions and approved deviations

- Option A approved by the maintainer on 2026-09-01: defer public host-instance identity, keep the existing envelope field explicitly unavailable for Stage 4, and publish no live state in this phase. See `DIVERGENCES.md` D1.
- The public client contract remains separate from private adapter IPC.
- The public administration surface remains limited to the existing canonical messages and host services; no speculative public list/revoke/block/reset protocol is added.
- The Stage 4 public message matrix in `PLAN.md` is authoritative for implementation allowlists; server-originated messages must not be accepted as client requests.
- D2 is approved: restricted sessions allow the complete pairing allowlist from the canonical schema, including `pairing_ack`, `pairing_renotify`, and `pairing_cancel`; the abbreviated source wording has been reconciled.
- `bridgeVersion` remains required and uses the existing transitional `bridge/vcpkg.json` value without a phase-branch version bump; `bridgeInstanceId` remains the approved D1 limitation.
- D3 is approved: Stage 4's public transport implements the approved 128-message/2 MiB outbound bound as one flat pool rather than the reserved-control/Normal/Heavy lane split, since no live data lane exists yet to compete for capacity. See `DIVERGENCES.md` D3.
- Root `PLAN.md` is marked historical/paused reference, not implementation authority; `ROADMAP.md`, `host/PLAN.md`, and this package are authoritative for Stage 4 work.
- Approved by the maintainer on 2026-09-02: a 10-second pre-authentication hello deadline, closing a
  security/availability decision gap identified by a `/think` investigation the same day (a
  transport-live connection that never sends `hello` could otherwise hold the single public
  admission slot indefinitely by continuing to answer WebSocket Ping/Pong). The deadline starts only
  once the WebSocket upgrade has completed -- distinct from Concept 01's approved 5-second upgrade
  handshake timeout and 60-second liveness ceiling, neither of which changed -- and is owned by
  Concept 02, not transport: a valid `hello` accepted before the deadline atomically cancels it, a
  deadline that fires first closes that exact connection and releases its slot, and the deadline is
  scoped to one connection's exact lifetime so it can never affect a later reconnect. Recorded in
  `ai/context/protocol/security.md`'s "Input limits" and "Connection liveness" sections and in
  `02-authentication-and-session-admission.md`'s Contracts, Invariants, and Proof obligations. This
  pass recorded the requirement only; Concept 02 as a whole remains not started (see "Active
  concept" below), and no production code changed.

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
- Concept 01, transport-context corrective pass, new: `host/DovahLink.Host/Client/Transport/PublicConnectionContext.cs`;
  `host/DovahLink.Host.Tests/Client/Transport/PublicConnectionContextTests.cs`.
- Concept 01, transport-context corrective pass, modified: `IPublicWebSocketMessageHandler.cs`,
  `PublicWebSocketConnection.cs` (`RequestClose`, the `IPublicConnectionContext` wiring, and the
  `writerCancellation`/`readCancellation` split); matching test file
  `PublicWebSocketConnectionTests.cs`; existing test doubles `FakePublicWebSocketConnection.cs`,
  `ThrowingPublicWebSocketConnection.cs`, `FakePublicWebSocketMessageHandler.cs`.
- `ai/context/host/architecture.md`: recorded the general per-connection capability boundary invariant
  (no concrete type names) this corrective pass then implemented.
- Three further focused fix passes, modified: `PublicWebSocketConnection.cs` (`orderlyCloseInProgress`
  flag and its `Volatile` access, `writerTask` promoted to a field, `InterruptReadOnceOutboundDrainsAsync`
  now waits on it, `ReadLoopAsync`'s `HandleMessageAsync` call bounded by `Task.WaitAsync(cancellationToken)`);
  `IPublicWebSocketMessageHandler.cs` (`HandleMessageAsync`'s documented no-synchronous-blocking
  contract); `PublicWebSocketConnectionTests.cs`; test doubles `BlockingAfterFirstWriteStream.cs`
  (`Release()` now actually forwards the blocked write instead of discarding it) and
  `FakePublicWebSocketMessageHandler.cs` (`HangOnHandleMessageIgnoringCancellation`).
- Outbound message-count corrective pass, modified: `PublicWebSocketConnection.cs`
  (`outboundOutstandingMessages` field, `TrySend`'s reservation/rollback, `WriterLoopAsync`'s release);
  `PublicWebSocketTransportOptions.cs` (`OutboundQueueMaxMessages` doc clarified as a total
  queued-plus-in-flight bound); `PublicWebSocketConnectionTests.cs` (rewrote the message-count-overflow
  test to prove the corrected bound, added the slot-release-timing, concurrent-admission,
  exact-boundary, and byte-rejection-does-not-leak-the-count tests). No new test-double file: reused
  the existing `BlockingAfterFirstWriteStream`.
- Pre-authentication hello deadline decision pass, documentation-only: `ai/context/protocol/security.md`
  (new "Input limits" bullet and new "Connection liveness" bullet); `02-authentication-and-session-admission.md`
  (new Contracts bullet, new Invariants bullet, five new Proof obligations bullets); this `CONTEXT.md`
  (this decision entry, this file list, the verification note above). No `host/DovahLink.Host` or
  `.Tests` file changed; Concept 02 remains not started.
- `NotifyDisconnectedAsync` disposal-race fix, modified: `PublicWebSocketConnection.cs`
  (`notifyDeadline.Cancel()` added in a new `finally` block). No test file changed: the existing
  `RunAsync_DisconnectNotificationHangs_StillTearsDownWithinBound` already covers this exact
  contract and now passes deterministically under repetition instead of racing.

## Verification

- `git branch --show-current`: `feature/4-host-client-boundary-and-pairing`
- `host/DovahLink.Host.Tests`: 528 passed before Concept 01; 592 passed after Concept 01's original
  three steps and follow-up fix pass; 622 passed after the post-conventions-cleanup corrective pass
  (60-second liveness deadline fix, deterministic loopback-bind proof, plus the RFC 6455 and
  fragmented-message hardening and the mandatory `HandleConnectionEnded()` seam already present
  going into this pass); 624 passed after this liveness-precision corrective pass (the exact-sum
  claim replaced with a budgeted-with-headroom one, plus two tests pinning the approved 50s/5s split);
  638 passed after the transport-context corrective pass (14 new: the per-connection capability's
  forwarding/no-raw-transport-leak proof, `RequestClose`'s drain/idempotency/racing/bounded-fallback
  behavior, and the handler-wiring/stale-context-isolation proofs), re-run five times with no flake;
  639 passed after the `TrySend`-misclassification/drain-wait fix (1 new: the blocked-writer
  regression proving a late send rejected by an in-progress orderly close no longer aborts an
  in-flight terminal frame), re-run eight times with no flake; unchanged at 639 after the
  `orderlyCloseInProgress` `Volatile` fix (no behavior change, only cross-thread visibility), the
  affected concurrency tests re-run eight times with no flake; 640 passed after the hung-handler
  `Task.WaitAsync` fix (1 new: a handler whose returned Task never completes and ignores
  cancellation no longer blocks `RunAsync` during shutdown), re-run three times with no flake;
  unchanged at 640 after this documentation-boundary correction pass (one test renamed for
  precision, no assertions changed); 644 passed after the outbound message-count corrective pass (4
  new: the corrected in-flight-frame-counts-toward-the-limit regression proof, slot-release-only-after-
  send-finishes, concurrent-admission-never-exceeds-the-bound, and byte-rejection-does-not-leak-the-
  message-count-reservation), re-run five times with no flake
- `integration/DovahLinkValidationClient.Tests`, `ctest --test-dir adapter/build/windows-x64-debug`:
  not re-run since the transport-context corrective pass -- every pass since changes only
  `host/DovahLink.Host`/`.Tests` C# transport internals and documentation, with no protocol, SDK, or
  native/adapter change
- `python -m unittest discover -s tooling -p "test_*.py"`: 93 passed, re-run during the
  transport-context corrective pass; unchanged; re-run again after the pre-authentication hello
  deadline decision pass below (documentation-only; appended to but did not alter any phrase
  `test_repository_consistency.py` locks in `security.md`)
- Unchanged at 644 passed after the `NotifyDisconnectedAsync` disposal-race fix (0 new tests: the
  existing regression test already covered this contract), re-run 15 times filtered to that test
  alone (previously 5s-then-fail, now consistently ~260-400ms) plus 3 full-suite runs, no flake in
  either
- `dotnet build ... -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=true`: clean, re-run
  after every pass since the transport-context corrective pass
- `dotnet build host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --configuration Release` and
  the resulting `DovahLink.Host.exe`: clean, re-run during the transport-context corrective pass
- `host/PLAN.md` SHA-256: `7434ECE0A3ACDBF9A7D86460F080D1BC7310B4AF6C2A15BF8868C676DCB1CC0C`

## Handoff

Next concept: `02-authentication-and-session-admission.md`
Blocked by: explicit maintainer authorization naming Concept 02 as the requested scope. Per the
package's own execution guardrails, implementation does not auto-proceed from one concept to the
next; the maintainer must confirm before Concept 02 begins.
