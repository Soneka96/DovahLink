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

- File: `03-pairing-and-client-dispatch.md`
- Status: not started; awaiting explicit maintainer authorization to begin
- Prerequisites: Concept 02 complete; Stage 2 authentication/trust/identity/session services and Stage 3 host composition available; feature branch `feature/4-host-client-boundary-and-pairing` active; cold-start handoff gate satisfied
- Next action: Revalidate the source fingerprint and this ledger, then implement only Concept 03 once the maintainer names it as the requested scope.

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
- `02-authentication-and-session-admission.md`: complete, built as seven independently reviewed and
  committed `/step-build` steps. Implements typed envelope validation, hello authentication across
  all three approved methods, replay protection, trust-tier admission, fresh session identity, the
  approved 10-second pre-authentication admission deadline, the 3-violations-in-30-seconds close
  policy, and the per-tier inbound allowlist including the mechanical post-admission `capabilities`/
  `subscribe`/`snapshot_request` exchanges. Mapping `ping`/pairing/`rename_request` to their owning
  services is explicitly Concept 03's scope: those types pass the allowlist here but produce no
  response yet.
  - Step 1 (flagged amendment, outside Concept 02's literal file allowlist): the approved
    pre-authentication deadline needs the handler to obtain a `RequestClose()`-capable connection
    reference *before any message exists* -- a deadline can fire with zero messages ever sent -- but
    `IPublicWebSocketMessageHandler` (Concept 01's own contract) had no hook earlier than
    `HandleMessageAsync`. Added one new, symmetric lifecycle member,
    `HandleConnectionEstablished(IPublicConnectionContext)`, called once by
    `PublicWebSocketConnection.RunAsync` immediately after the WebSocket upgrade completes and before
    the read loop starts (mirroring `HandleConnectionEnded()`'s existing tolerant-of-throwing
    contract). This is the same class of "a completed concept's seam is insufficient" gap Concept
    01's own transport-context corrective pass hit; flagged to the maintainer before implementation
    and approved as part of continuing the step plan. Regression: the one existing exact-`CallOrder`
    assertion this changes (`RunAsync_ClientClose_CallsConnectionEndedBeforeDisconnectedAndBeforeReturning`)
    was updated to include the new call; four new tests cover the hook firing exactly once before any
    message, not firing when the handshake never completes, tolerating a throwing handler, and the
    actual intended use (a handler scheduling a delayed `RequestClose()` from within the hook, plus
    the synchronous-`RequestClose()`-from-within-the-hook ordering edge case against the not-yet-
    assigned writer task).
  - Step 2: `ActiveSessionRecord`/`ISessionRegistry.TryCreate` gained explicit
    `SessionAuthenticationSource`/`SessionTrustTier` fields with no convenience default (security-
    significant admission state). `InvalidateAllForClient` now exempts a `OneTimeLocalToken` session
    from client-scoped Block/Revoke even when its self-declared `clientId` matches the target, while
    `InvalidateAll` (Factory Reset) stays unconditional -- resolving the first half of the "Deferred
    debt" bullet below. Removed `SessionRegistry.Create(ClientId, ConnectionId)`, a dead, untested,
    zero-caller convenience method that could not otherwise keep a silent default for the two new
    required parameters.
  - Step 3 (refactor): extracted `PairingCoordinator`'s private `HashCredential`/`FixedTimeEquals`
    into a new stateless `Trust/CredentialHasher.cs`, so verifying a `trusted_device_credential` hello
    and `PairingCoordinator`'s own credential/code checks share one implementation. No behavior
    change; `TrustResetService`'s own separate inline `FixedTimeEquals` (factory-reset code
    comparison) was deliberately left alone as out of scope for this pass.
  - Step 4: added `ITrustedCredentialFailureThrottle`/`TrustedCredentialFailureThrottle`
    (`Client/Authentication/`), a global rolling-window failure counter mirroring
    `ILocalConnectionTokenAuthenticator`'s own shape, kept as a separate budget from the developer-
    token throttle per `ai/context/protocol/security.md`.
  - Step 5: added the bounded `PublicEnvelopeCodec` and every message-specific payload DTO
    (`Client/Protocol/`). Enforces JSON nesting depth (32), string byte length (4 KiB), array length
    (128), and object member count (64) before materializing any typed value. Payload DTOs use C#
    `required` init-only properties rather than positional-record constructor parameters: an initial
    assumption that STJ enforces "required" on plain constructor parameters proved wrong under test
    (a missing `clientId` silently deserialized to `null` instead of failing), corrected before this
    step's tests were reported passing.
  - Step 6 (the concept's core): added `PublicHelloAdmissionHandler`, one instance per connection,
    implementing `IPublicWebSocketMessageHandler`. Preserves the exact admission ordering the
    security contract requires: `ILocalConnectionTokenAuthenticator` gained `TryValidate`/
    `CommitConsumption` alongside its existing `TryConsume` so a one-time token is validated without
    being consumed, and is committed only once `SessionRegistry.TryCreate` and the post-reservation
    trust recheck both succeed -- a full session slot or a losing race against a concurrent
    administrative Block/Revoke both roll back cleanly via `SessionRegistry.Invalidate` without
    burning a retryable token. The admission deadline and a successful `hello` race through one
    `Interlocked.CompareExchange` outcome flag so exactly one ever wins. A fresh-eyes pass caught a
    real bug before this step was reported done: `hello_ack.clientIdentityKind` was derived from the
    session's trust tier rather than its authentication source, which would have reported a
    developer-token session as `"paired"` instead of the schema's required `"unpaired"`.
  - Step 7: added the per-tier inbound allowlist (`IsAllowedForTier`) and the `capabilities`/
    `subscribe`/`snapshot_request` dispatch. Final-review pass found `admissionDeadlineCts` was
    written in `HandleConnectionEstablished` and read/cancelled from `Admit`/`HandleConnectionEnded`
    without going through the handler's own `gate` lock, despite that field's own doc comment
    claiming full coverage; fixed to route through `gate` consistently before this step was reported
    done.
  - Every step's fresh-eyes subagent pass and full-suite run are recorded under "Verification" below;
    no divergence from `DIVERGENCES.md` was required, and no file outside Concept 02's allowlist was
    touched except the flagged Step 1 amendment above.
  - Post-completion corrective pass (`/step-build`, eight steps, 2026-09-03): a maintainer-directed
    `/think` review against the merged PR found seven real defects and confirmed several other
    suspected issues were already correctly handled; each real defect was fixed in its own step with
    focused regression tests, all within Concept 02's existing allowlist.
    - Step 1: `CredentialHasher.IsValidHexCredential` validates a presented `trusted_device_credential`
      as exactly `Constants.PairingCredentialLength` hex characters before it ever reaches hashing, and
      `ITrustedCredentialFailureThrottle` gained `TryAttempt(Func<bool>)`, running the failure-window
      check, the credential comparison, and any failure recording under one lock -- closing a real
      TOCTOU race the previous `IsAllowed()`/compare/`RecordFailure()` split left open, where several
      concurrent callers could each observe the window as open before any outcome was recorded and so
      exceed the configured five-failure bound. A concurrency test proves the verify callback is
      invoked at most five times across fifty concurrent failing attempts.
    - Step 2: `ILocalConnectionTokenAuthenticator` gained `RollbackReservation()`, and `TryValidate`
      now atomically reserves a matched token so a second concurrent `TryValidate` call presenting the
      identical correct token fails while the reservation is outstanding -- a real latent race
      previously masked only by `Constants.MaxActiveSessions == 1`, never itself tested as the security
      mechanism. Two existing tests that encoded the old implicit-non-consumption behavior were
      rewritten around the new explicit rollback contract; a concurrency test proves only one of ten
      simultaneous callers presenting the same token succeeds.
    - Step 3: wired both primitives into `PublicHelloAdmissionHandler` -- `IsValidAuth` now rejects a
      malformed-shape credential before authentication services, `HandleTrustBackedHello` routes its
      comparison through `TryAttempt`, and `HandleOneTimeLocalTokenHello` rolls back its reservation on
      every downstream admission failure. Every existing `trusted_device_credential` test fixture
      (previously plain-text values like `"the-credential"`) was updated to valid hex, since the new
      format check would otherwise reject them as malformed; two end-to-end concurrency tests prove
      the fixes hold through the full admission path, not merely at the primitive level.
    - Step 4: `PublicHelloAdmissionHandler` now enforces envelope identity semantics the schema always
      required but the handler never checked -- a pre-authentication `hello` must carry a null
      envelope `sessionId`, `bridgeInstanceId`, `clientId`, `correlationId`, and `playContextId`; a
      post-admission message must carry a null `bridgeInstanceId`/`playContextId` and, if present, an
      envelope `clientId` matching the connection's own admitted identity. Both were previously
      unvalidated -- a client could present foreign or premature identity values that were silently
      ignored. Nine new tests cover each field independently, both pre- and post-admission.
    - Step 5: `IsPostAdmissionClientMessageType` (derived from the existing per-tier allowlist, not a
      duplicated list) distinguishes a genuine trust-tier authorization failure from a protocol
      shape/direction violation. A structurally valid message the current tier does not allow (for
      example a restricted session sending `subscribe`) now returns `unauthorized`, which existed in
      `PublicProtocolErrorCode` but had zero references anywhere before this step; a server-originated
      type sent by a client (`hello_ack`, `state_snapshot`, etc.) remains `malformed_message`, since no
      tier could ever authorize it. Both previously collapsed to `malformed_message`.
    - Step 6: closed the same root cause behind two separately reported findings -- a Factory Reset
      race during admission and post-admission session staleness -- with one coherent fix.
      `HandleOneTimeLocalTokenHello` and `HandleTrustBackedHello` both recheck
      `SessionRegistry.IsActive(newSessionId, connectionId)` immediately before finalizing admission,
      so an unconditional `InvalidateAll()` landing in that window can never result in a `hello_ack`
      for a session the registry no longer knows about; `HandleMessageAsync`'s post-admission check
      now also requires `IsActive(sessionId, connectionId)`, so a session invalidated after admission
      is rejected on its very next message even though the connection's own local `admitted` state
      never changed. A `SessionRegistryThatInvalidatesAllOnFirstIsActiveCall` test double deterministically
      simulates the race for both the trust-backed and one-time-token hello paths.
    - Step 7: `TryRecordMessageId` now checks the 10,000-message bound before recording a new id, not
      only after, so a message already in flight through the read loop when an earlier message reached
      the bound (and requested the connection's orderly close) can never be recorded or dispatched --
      closing a narrow residual race the "accept the message that reaches the bound, close afterward"
      contract did not otherwise prevent. Exact boundary tests cover messages 9,999, 10,000, and a
      simulated 10,001 (including one carrying a previously seen `messageId`, proving it is dropped
      rather than misclassified as replay).
    - Step 8 (this entry): documentation-only. `02-authentication-and-session-admission.md` gained
      Contracts/Invariants/Proof-obligations text for the registry-liveness recheck, the
      unauthorized/malformed distinction, and the message-bound race; its completion criteria no
      longer require a startup trust-persistence proof this concept's files cannot satisfy (see
      `DIVERGENCES.md`'s new D4, which hands that proof to Concept 04). Four findings from the same
      review were confirmed already correct with no code change: `TrySend` failure during `Admit()` is
      already covered by Concept 01's existing forced-close-on-unadmittable-message guarantee
      (`RequestForcedCloseForUnadmittedMessage`), which drives `HandleConnectionEnded()`'s session
      invalidation; the distinct `Blocked`/`Revoked` error codes are an intentional, contract-mandated
      distinction, not a secret-comparison leak; the credential throttle's exemption of
      unknown/blocked/revoked identities (they return before ever reaching `IsAllowed()`/`RecordFailure()`)
      is deliberate -- no secret comparison happens for those cases, so there is nothing to brute-force
      -- and is now recorded here as the documented rationale; and `Guid.TryParse`'s acceptance of
      multiple textual GUID forms creates no identity-aliasing risk, since `ClientId` compares the
      parsed `Guid` struct, never the original wire string. Static `Guid.NewGuid()` for host-originated
      `messageId`s matches this project's own established convention (every identity type --
      `SessionId`, `ConnectionId`, `ClientId`, `PlayContextId`, `AdapterInstanceId` -- already generates
      the same way); introducing an injected generator here would be a deviation from that convention,
      not an alignment with it.
  - Second post-completion corrective pass (`/step-build`, four steps, 2026-09-03): a second
    maintainer-directed re-review of the first corrective pass, this time against the merged working
    tree rather than only the diff, found three further real defects the first pass's own fresh-eyes
    reviews and tests did not catch. All three verified directly against the current code (not
    assumed from the review) before any fix, and one review claim was independently checked and
    found unsupported before acting on it (see Step 3 below).
    - Step 1: `ILocalConnectionTokenAuthenticator.TryConsume` never checked the `reserved` flag
      `TryValidate` introduced in the first pass's Step 2, so it could consume a token out from under
      an outstanding `TryValidate` reservation. `TryConsume` has zero production callers today (only
      `TryValidate`/`CommitConsumption`/`RollbackReservation` are wired into
      `PublicHelloAdmissionHandler`), so this was a latent hygiene gap rather than an active exploit,
      but leaving an inconsistent single-use guarantee on a security-critical type was a live footgun
      for any future caller. Fixed to also return `false` while reserved, without recording a
      rate-limit failure for that case.
    - Step 2: found and fixed the genuine blocking defect the first pass's own Factory Reset race
      fix (Step 6 of the first pass) left behind. `TryClaimAdmission()` claims this connection's
      one-shot `admissionOutcome` (`Pending -> Admitted`) *before* the registry-liveness recheck runs;
      when that recheck then loses to a concurrent Factory Reset, the existing code rolled back and
      rejected but never requested the connection's close, and `admissionOutcome` is deliberately
      never reset back to `Pending`. The connection was left with no path to ever being torn down: the
      pre-authentication deadline's own `CompareExchange(..., Pending)` could never fire again, and a
      retried `hello` on the same socket hit `!TryClaimAdmission()` and silently no-opped forever
      instead of failing loudly or succeeding. Fixed by calling `connectionContext.RequestClose()` in
      that losing branch in both hello authentication paths, deliberately without resetting the
      outcome. New tests prove the losing connection is actually requested to close, a second `hello`
      on that same doomed connection creates no new session (both hello methods), and the token
      remains usable through a genuinely separate second connection, not merely via a direct
      `TryValidate` call.
    - Step 3: post-admission envelope `clientId` was wrongly optional
      (`envelope.ClientId is not null && envelope.ClientId != currentClientId.ToString()` let a null
      value through) and, when present, was string-compared against the admitted identity's canonical
      `ToString()` form rather than parsed and compared structurally -- reopening exactly the
      GUID-textual-form aliasing gap the first pass's own Step 8 traceability note had just declared
      closed everywhere, since that note was written before this specific post-admission comparison
      was audited. The reviewer's claimed quote for "clientId is required post-admission" was checked
      against `protocol/schema/README.md` and `security.md` first and found unsupported there; it was
      only confirmed by finding the actual authoritative sentence in `PLAN.md:184-185` ("After
      admission, client messages carry the socket-bound sessionId and their declared clientId"), a
      file the first pass's review had not checked. Fixed to require a non-null `clientId`, parse it,
      and compare the parsed `Guid` against `admittedClientId.Value`. This was the largest fixture
      change in either pass: `AdmitViaUnpairedHello`/`AdmitViaTrustedDeviceCredentialHello` now also
      return the admitted `clientId`, and every post-admission test that expects a message to actually
      be processed -- including the four message-bound hot-loop tests sending thousands of messages
      each -- now carries it; tests expecting an already-correct rejection for an unrelated earlier
      reason (foreign/stale session, externally invalidated session, malformed raw-JSON payloads) were
      deliberately left unchanged. New tests cover a missing clientId, a non-GUID shape, and the
      admitted identity presented in braces and compact (no-hyphens) textual forms, both still
      accepted.
    - Step 4 (this entry): full suite and repeated-concurrency-suite verification, docs+warnings
      build, tooling suite, and this traceability update. No further defects found on this pass's own
      fresh-eyes review of the three fixes.

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
- The pre-authentication hello deadline decision above is now implemented and tested by Concept 02's
  Steps 1 and 6 (see "Completed concepts"); this entry is left as the historical decision record per
  this document's own append-only convention.
- Approved via direct maintainer instruction in the current task on 2026-09-03: the post-completion
  corrective pass's seven fixes and eight-step plan, following a same-day `/think` review. See
  `DIVERGENCES.md`'s D4 for the startup trust-persistence completion-criterion handoff to Concept 04
  this pass also made.
- Approved via direct maintainer instruction in the current task on 2026-09-03: the second
  post-completion corrective pass's three fixes and four-step plan, following a same-day second
  `/think` re-review that found real defects in the first pass's own fixes.

## Deferred debt

- Public host-instance identity and any required canonical envelope/protocol revision remain deferred until before live state publication/cutover.
- Live state publication, bounded delivery, and real capture remain Stage 5 and later.
- Pairing availability must be tied to an accepted adapter display/redisplay operation; a challenge must not be reported as available merely because a code was generated in host memory.
- Pairing forwarding includes initial display, manual redisplay, wrong-code automatic redisplay, and the no-code attempts-exhausted notification; none of these values may leak onto the public wire.
- Resolved by Concept 02, Step 2: session records now retain authentication source and trust tier
  (`ActiveSessionRecord`), and `SessionRegistry.InvalidateAllForClient` exempts a developer-token
  session from client-scoped Block/Revoke through a matching self-declared `clientId`. Still open:
  administrative invalidation's reason-specific best-effort `session_invalidated` notification before
  close requires routing from a `clientId`/`sessionId` to the exact live connection that owns it,
  which no current concept builds yet -- Concept 02's `PublicHelloAdmissionHandler` only invalidates
  its own connection's session on local teardown (`HandleConnectionEnded`), never a session it does
  not own. This routing, and wiring `TrustAdminService`'s mutations to it, belongs to Concept 04
  ("completes the host composition root"), per the concept graph in `PLAN.md`.
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
- Resolved by Concept 02, Step 6: `PublicHelloAdmissionHandler.HandleConnectionEnded()` invalidates
  the exact socket's session via `SessionRegistry.Invalidate(sessionId, connectionId)`, is a fast
  local/in-memory operation with no I/O, is a no-op when never admitted, and cannot throw under
  normal session-registry conditions (`FakeSessionRegistry`'s own `Invalidate` never throws).
  `HandleDisconnectedAsync()` is an immediate `Task.CompletedTask`; best-effort secondary cleanup
  beyond mandatory invalidation is not yet needed since Concept 02 introduces no other per-connection
  resource to release.

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
- Concept 02, Step 1 (flagged transport amendment), modified:
  `host/DovahLink.Host/Client/Transport/IPublicWebSocketMessageHandler.cs` (new
  `HandleConnectionEstablished` member), `PublicWebSocketConnection.cs` (`NotifyConnectionEstablished`
  and its call site); test doubles `FakePublicWebSocketMessageHandler.cs`; test file
  `PublicWebSocketConnectionTests.cs` (one updated `CallOrder` assertion, four new tests).
- Concept 02, Step 2, modified: `host/DovahLink.Host/Enums.cs` (`SessionAuthenticationSource`,
  `SessionTrustTier`), `Sessions/ActiveSessionRecord.cs`, `Sessions/SessionRegistry.cs`
  (`TryCreate`'s new required parameters, `InvalidateAllForClient`'s developer-token exemption,
  `Create(ClientId, ConnectionId)` removed); test doubles `FakeSessionRegistry.cs` (matching
  signature/exemption, new `ActiveCount`); test file `SessionRegistryTests.cs`.
- Concept 02, Step 3, new: `host/DovahLink.Host/Trust/CredentialHasher.cs`; test file
  `host/DovahLink.Host.Tests/Trust/CredentialHasherTests.cs`. Modified:
  `host/DovahLink.Host/Pairing/PairingCoordinator.cs` (regression only, no behavior change).
- Concept 02, Step 4, new: `host/DovahLink.Host/Client/Authentication/TrustedCredentialFailureThrottle.cs`;
  test file `host/DovahLink.Host.Tests/Client/Authentication/TrustedCredentialFailureThrottleTests.cs`.
  Modified: `Constants.cs` (`TrustedCredentialMaxFailuresPerWindow`/`TrustedCredentialFailureWindow`).
- Concept 02, Step 5, new: `host/DovahLink.Host/Client/Protocol/PublicEnvelope.cs`, `HelloPayload.cs`,
  `HelloAuthPayload.cs`, `HelloAckPayload.cs`, `CapabilitiesPayload.cs`, `SubscribePayload.cs`,
  `SubscriptionAckPayload.cs`, `SnapshotRequestPayload.cs`, `ErrorPayload.cs`,
  `PublicEnvelopeCodec.cs`; test file `host/DovahLink.Host.Tests/Client/Protocol/PublicEnvelopeCodecTests.cs`.
  Modified: `Enums.cs` (`PublicMessageType`, `HelloAuthMethod`, `ClientIdentityKind`,
  `PublicProtocolErrorCode`), `Constants.cs` (JSON-bound constants).
- Concept 02, Step 6, new: `host/DovahLink.Host/Client/Authentication/PublicHelloAdmissionHandler.cs`;
  test file `host/DovahLink.Host.Tests/Client/Authentication/PublicHelloAdmissionTests.cs`. Modified:
  `host/DovahLink.Host/Authentication/LocalConnectionTokenAuthenticator.cs` (`TryValidate`/
  `CommitConsumption`), `Constants.cs` (admission deadline, violation-window, session-message-bound,
  transitional bridge-version constants); test file `LocalConnectionTokenAuthenticatorTests.cs`.
- Concept 02, Step 7, modified: `PublicHelloAdmissionHandler.cs` (`IsAllowedForTier`, post-admission
  dispatch, the `gate`-locking fix for `admissionDeadlineCts`); `PublicHelloAdmissionTests.cs`.
- `02-authentication-and-session-admission.md`: `Status: pending` → `Status: complete`.
- Post-completion corrective pass, Step 1, modified: `host/DovahLink.Host/Trust/CredentialHasher.cs`
  (`IsValidHexCredential`); `host/DovahLink.Host/Client/Authentication/TrustedCredentialFailureThrottle.cs`
  (`ITrustedCredentialFailureThrottle.TryAttempt`); test files `CredentialHasherTests.cs`,
  `TrustedCredentialFailureThrottleTests.cs`.
- Post-completion corrective pass, Step 2, modified:
  `host/DovahLink.Host/Authentication/LocalConnectionTokenAuthenticator.cs`
  (`ILocalConnectionTokenAuthenticator.RollbackReservation`, reservation-aware `TryValidate`); test
  file `LocalConnectionTokenAuthenticatorTests.cs` (two tests rewritten, four new).
- Post-completion corrective pass, Step 3, modified: `PublicHelloAdmissionHandler.cs` (`IsValidAuth`,
  `HandleTrustBackedHello`'s `TryAttempt` wiring, `HandleOneTimeLocalTokenHello`'s
  `RollbackReservation` calls); test file `PublicHelloAdmissionTests.cs` (every
  `trusted_device_credential` fixture updated to valid hex, two new malformed-credential cases, two
  new end-to-end concurrency tests).
- Post-completion corrective pass, Step 4, modified: `PublicHelloAdmissionHandler.cs`
  (`admittedClientId` field, `IsValidPreAuthEnvelopeIdentity`, the post-admission envelope-identity
  check); test file `PublicHelloAdmissionTests.cs` (nine new tests).
- Post-completion corrective pass, Step 5, modified: `PublicHelloAdmissionHandler.cs`
  (`IsPostAdmissionClientMessageType`, the `Unauthorized`/`MalformedMessage` classification split);
  test file `PublicHelloAdmissionTests.cs` (two theories split into four, one new test double case).
- Post-completion corrective pass, Step 6, modified: `PublicHelloAdmissionHandler.cs` (the
  `SessionRegistry.IsActive` recheck in both hello authentication paths and in post-admission message
  authorization); test file `PublicHelloAdmissionTests.cs` (new
  `SessionRegistryThatInvalidatesAllOnFirstIsActiveCall` test double, four new tests).
- Post-completion corrective pass, Step 7, modified: `PublicHelloAdmissionHandler.cs`
  (`TryRecordMessageId`'s `out bool boundAlreadyExceeded` and pre-check); test file
  `PublicHelloAdmissionTests.cs` (three new exact-boundary tests).
- Post-completion corrective pass, Step 8 (this pass), modified:
  `02-authentication-and-session-admission.md` (new Contracts/Invariants/Proof-obligations text, the
  completion-criteria ownership handoff); `04-adapter-notification-and-composition.md` (the received
  startup trust-persistence proof obligation and completion criterion); `DIVERGENCES.md` (new D4);
  this `CONTEXT.md` (this decision entry, this file list, the verification counts below). No public
  schema, SDK, or Concept 03 file touched.
- Second post-completion corrective pass, Step 1, modified:
  `host/DovahLink.Host/Authentication/LocalConnectionTokenAuthenticator.cs` (`TryConsume`'s `reserved`
  check); test file `LocalConnectionTokenAuthenticatorTests.cs` (three new tests).
- Second post-completion corrective pass, Step 2, modified: `PublicHelloAdmissionHandler.cs`
  (`connectionContext.RequestClose()` added to the losing Factory Reset recheck branch in both hello
  authentication paths); test file `PublicHelloAdmissionTests.cs` (both existing races extended with
  a `RequestCloseCalls` assertion; three new tests: second-hello-creates-no-session for each hello
  method, and token-usable-on-a-genuinely-separate-connection).
- Second post-completion corrective pass, Step 3, modified: `PublicHelloAdmissionHandler.cs` (the
  post-admission `clientId` check now requires a non-null value and compares the parsed `Guid`
  against `admittedClientId.Value`); test file `PublicHelloAdmissionTests.cs` (the two `Admit*Hello`
  helpers now also return the admitted `clientId`; every post-admission test expecting a message to
  be processed updated to carry it; five new tests for missing/non-GUID/foreign/alternate-textual-form
  clientId).
- Second post-completion corrective pass, Step 4 (this pass), modified:
  `02-authentication-and-session-admission.md` (three new Contracts bullets and three new Proof
  obligations bullets covering these fixes); this `CONTEXT.md` (this decision entry, this file list,
  the verification counts below). No public schema, SDK, or Concept 03 file touched.

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
- Full suite subsequently reached 650 passed once the six fragment-assembly tests described under
  "Completed concepts" above were added (644 baseline + 6 new: 5 in-scope tests from the plan plus 1
  fresh-eyes addition covering external cancellation mid-assembly) -- this is the later, current
  count; the 644 entry above is left as the historical baseline it was recorded against, per this
  document's own append-only convention for this section.
- `dotnet build ... -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=true`: clean, re-run
  after every pass since the transport-context corrective pass
- `dotnet build host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --configuration Release` and
  the resulting `DovahLink.Host.exe`: clean, re-run during the transport-context corrective pass
- `host/PLAN.md` SHA-256: `7434ECE0A3ACDBF9A7D86460F080D1BC7310B4AF6C2A15BF8868C676DCB1CC0C`
- Concept 02 baseline re-verified at session start (fingerprint-check artifact: hashing the
  Windows-checked-out working-tree file directly, with `core.autocrlf=true`, gives a different value
  than the committed blob due to CRLF line endings; `git show HEAD:host/PLAN.md | sha256sum` matches
  the recorded fingerprint above exactly -- no real drift. Noted here so a future pass on a Windows
  checkout does not misread the same artifact as a genuine mismatch): 679 passed (post-PR #46 merge,
  pre-Concept-02).
- Concept 02 progression, each count independently re-verified via `dotnet test` after its step's own
  fresh-eyes pass and convention audit: 684 after Step 1 (5 new: the connection-established hook);
  689 after Step 2 (5 new: authentication source/trust tier and the developer-token exemption); 698
  after Step 3 (9 new: `CredentialHasher`, re-run as a pure/stateless unit with no repetition needed);
  707 after Step 4 (9 new: the credential throttle, including a corrected staggered-pruning boundary
  test caught by its own fresh-eyes pass); 765 after Step 5 (58 new: the envelope codec and payload
  DTOs, including the exhaustive 20-value `PublicMessageType` round-trip theory and the UTF-8-byte-
  vs-char-count boundary test its fresh-eyes pass added); 806 after Step 6 (41 new: 5 in
  `LocalConnectionTokenAuthenticatorTests` for the new `TryValidate`/`CommitConsumption` pair, 36 in
  the new `PublicHelloAdmissionTests` after its fresh-eyes pass, which caught the
  `clientIdentityKind` bug and fixed it); 833 after Step 7 (27 new: post-admission dispatch and the
  allowlist, plus the
  `admissionDeadlineCts` locking fix). Timing-sensitive suites (`PublicHelloAdmissionTests`,
  `LocalConnectionTokenAuthenticatorTests`) re-run five times at Steps 1, 6, and 7 with no flake.
- `dotnet build ... -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=true`: clean, re-run
  after every one of Concept 02's seven steps.
- `integration/DovahLinkValidationClient.Tests`, `ctest --test-dir adapter/build/windows-x64-debug`,
  `python -m unittest discover -s tooling -p "test_*.py"`: not re-run during Concept 02 -- every
  change is confined to `host/DovahLink.Host`/`.Tests` C# application code, with no protocol, SDK, or
  native/adapter file touched.
- `host/PLAN.md` SHA-256 re-verified unchanged at Concept 02's close (git-blob level, matching the
  value above): no drift occurred while Concept 02 was implemented.
- Post-completion corrective pass: 833 passed at this pass's baseline (unchanged from Concept 02's own
  close). Steps 1-3 (the credential/token primitives and their wiring into the handler) were each
  verified against their own affected test files rather than a full-suite run at every intermediate
  step; the first full-suite checkpoint after them landed at 855 passed (+22 across all three,
  including the fifty-concurrent-attempt throttle-invocation proof, the ten-concurrent-attempt
  token-reservation proof, two end-to-end handler-level concurrency tests, and every existing
  `trusted_device_credential` fixture updated to valid hex). From there, each further step's full-suite
  count was independently re-verified via `dotnet test` after its own fresh-eyes pass and convention
  audit: 864 after Step 4 (+9: envelope identity, both pre-auth and post-admission); 869 after Step 5
  (+5: the `Unauthorized`/`MalformedMessage` classification split, two theories become four plus one
  added server-only case); 873 after Step 6 (+4: the Factory Reset race, both hello authentication
  paths, plus the full-session post-admission symmetry test); 876 after Step 7 (+3: the exact
  message-bound boundary and post-bound-race tests). Final full-suite run at the close of Step 8: 876
  passed, 0 failed, unchanged (documentation-only). Timing/concurrency-sensitive suites
  (`PublicHelloAdmissionTests`, `LocalConnectionTokenAuthenticatorTests`,
  `TrustedCredentialFailureThrottleTests`, 127 tests combined) re-run five times at the close of Step
  8 with no flake, in addition to the per-step repetitions recorded in each step's own commit.
- `dotnet build ... -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=true`: clean, re-run
  after every one of this pass's eight steps.
- `python -m unittest discover -s tooling -p "test_*.py"`: 95 passed, re-run at Step 8's close
  (documentation-only changes to the plan package; no phrase `test_repository_consistency.py` locks
  in `security.md` or elsewhere was altered).
- `integration/DovahLinkValidationClient.Tests`, `ctest --test-dir adapter/build/windows-x64-debug`:
  not re-run -- every change in this pass is confined to `host/DovahLink.Host`/`.Tests` C# application
  code and this phase's own planning documents, with no protocol, SDK, or native/adapter file touched.
- `git branch --show-current`: `feature/4-host-client-boundary-and-pairing`, unchanged; `git status`
  clean except this pass's own documentation edits at the time of writing; no file outside this
  concept's allowlist and this phase's planning package was touched.
- Second post-completion corrective pass: 876 passed at this pass's baseline (unchanged from the
  first corrective pass's own close). Step 1 was verified against its own affected test file
  (`LocalConnectionTokenAuthenticatorTests`, 28 passed including the 3 new tests) rather than an
  isolated full-suite run; the first full-suite checkpoint after it, taken at the close of Step 2,
  landed at 882 passed (+6 across both steps: 3 `TryConsume` reservation tests plus 3 Factory Reset
  `RequestClose`/no-new-session/separate-connection tests). 886 passed after Step 3 (+4: missing,
  non-GUID, and two alternate-textual-form clientId tests -- the large fixture-update itself added no
  new test count, only changed existing calls). Final full-suite run at the close of Step 4: 886
  passed, 0 failed, unchanged (documentation-only). Timing/concurrency-sensitive suites
  (`PublicHelloAdmissionTests`, `LocalConnectionTokenAuthenticatorTests`,
  `TrustedCredentialFailureThrottleTests`, 137 tests combined) re-run five times at the close of Step
  4 with no flake.
- `dotnet build ... -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=true`: clean, re-run
  after every one of this second pass's four steps.
- `python -m unittest discover -s tooling -p "test_*.py"`: 95 passed, re-run at this pass's close;
  unchanged (documentation-only planning-package edits, no `security.md` or schema phrase altered).
- `integration/DovahLinkValidationClient.Tests`, `ctest --test-dir adapter/build/windows-x64-debug`:
  not re-run -- every change in this pass is confined to `host/DovahLink.Host`/`.Tests` C# application
  code and this phase's own planning documents, with no protocol, SDK, or native/adapter file touched.
- Fresh traceability pass at this pass's close: every Contracts/Invariants/Proof-obligations/
  Non-goals/Completion-criteria line in `02-authentication-and-session-admission.md`, including the
  three new bullets this pass added, is satisfied by the current implementation and covered by a
  passing regression test. `git status`: clean except this pass's own documentation edits at the
  time of writing.

## Handoff

Next concept: `03-pairing-and-client-dispatch.md`
Blocked by: explicit maintainer authorization naming Concept 03 as the requested scope. Per the
package's own execution guardrails, implementation does not auto-proceed from one concept to the
next; the maintainer must confirm before Concept 03 begins.
