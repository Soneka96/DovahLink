# 04 — Host process lifecycle and composition

Status: complete

Covers: R1, R4, R5, R8

Depends on: `01-private-ipc-contract.md`, `02-host-ipc-channel.md`,
`03-native-adapter-core.md`.

## Owner and boundary

The adapter owns lifecycle plumbing for the packaged host process because it is
the Skyrim lifetime’s native entry point. The host owns its own deterministic
teardown once asked to stop. This concept is stable because process ownership,
adoption proof, retry, and cleanup are independent of any particular Skyrim
domain.

## Inputs and outputs

Inputs are adapter startup/shutdown callbacks, Papyrus connection requests,
packaged host executable metadata, private-channel readiness, and OS process
state. Outputs are a hidden host process tied to the current Skyrim lifetime,
bounded connection attempts, explicit host-readiness status, graceful shutdown
requests, and forced cleanup when the owner disappears.

## Contracts

- Launch uses structured process arguments and a hidden window; no untrusted
  shell command text is built or executed.
- The host creates the private listener from the configured loopback port. A
  configured port of `0` means the operating system assigns an available
  loopback port; the host then passes that actual bound port to the current
  adapter through a bounded, authenticated startup rendezvous. The adapter
  connects to the reported endpoint and never guesses or performs a
  find-then-bind port race.
- Adoption is allowed only after private-channel ownership/lifetime proof; a
  matching executable or PID alone is insufficient.
- Startup retry runs outside game-thread callbacks and has explicit bounds and
  cancellation.
- The adapter does not dispatch host-directed event or sample intents into
  Skyrim until the host-authentication result is accepted. This authentication
  gate is intentionally deferred from Concept 03 and is a mandatory Concept 04
  handoff requirement.
- A Papyrus or native connection request only triggers the same bounded,
  non-blocking lifecycle path; it does not add a second retry or policy layer.
- Orderly Skyrim close asks the host to perform deterministic teardown first.
  Parent-lifetime supervision remains the fallback for crash or forced exit.
- Repeated startup/shutdown and host-already-gone paths are safe and idempotent.

### Mutual authentication (D2)

`accepted = true` alone never proves the responder is the legitimate host --
it only proves the responder checked the adapter's presented proof. Per
`DIVERGENCES.md`'s D2, the Hello/HelloAck handshake recorded in
`01-private-ipc-contract.md` is extended to be mutually authenticated and
replay-resistant:

- The adapter generates a fresh random `challenge` (32 bytes) for every Hello
  it sends -- including every retry after a disconnect, never reused across
  attempts.
- The host computes `hostProof = HMAC-SHA256(key = peerProofToken, message =
  challenge || correlationId || adapterInstanceId || ownerLifetimeId)` and
  returns it in `IpcHelloAckMessage`, in addition to the existing
  `accepted`/`rejectReason` fields. A rejected `HelloAck` carries an
  all-zero `hostProof` (the host does not compute a real proof for a
  connection it is refusing).
- The adapter treats a connection as authenticated only when every one of the
  following holds -- a conjunction, not any single field:

  ```
  authenticated =
      accepted &&
      matching correlationId &&
      matching fresh challenge &&
      matching ownerLifetimeId &&
      constantTimeEqual(hostProof, expectedProof)
  ```

  `accepted` is checked explicitly and independently: a `hostProof` that
  happens to verify correctly on a response whose `accepted` is `false` must
  never be treated as available -- the two are ANDed, neither substitutes for
  the other. "Matching correlationId" is an explicit check that the received
  `HelloAck.correlationId` equals the `IpcHelloMessage.correlationId` this
  attempt sent. "Matching fresh challenge" and "matching ownerLifetimeId" mean
  `expectedProof` is recomputed using exactly the `challenge` and
  `ownerLifetimeId` this adapter generated/sent for *this* attempt (never a
  stale or different attempt's values) -- see "Exact HMAC encoding" below for
  `expectedProof`'s precise construction. A missing, forged, wrong, or
  replayed `hostProof`, or any mismatched field above, fails the whole
  conjunction and is treated as a rejected handshake, whatever `accepted`
  says on its own.
- Binding the proof to `correlationId` (a monotonic per-adapter-process
  counter that never repeats) and the fresh `challenge` (unpredictable and
  never repeats) defeats replay both within one adapter process's lifetime and
  across adapter restarts, where `correlationId` alone would otherwise reset
  to `1`.

#### Exact HMAC encoding

All integers are little-endian, matching the existing frame codec's
convention (`ipc_frame_codec.cpp`'s `WriteUInt64LittleEndian` etc.); no field
introduces a new byte-order decision.

- `challenge`: 32 raw random bytes, opaque (no integer interpretation).
- `correlationId`: the same 8-byte little-endian encoding already used for the
  frame header's correlation id.
- `adapterInstanceId`: the same 16 raw bytes already carried in
  `IpcHelloMessage`'s existing field, copied as-is -- no separate GUID
  re-encoding.
- `ownerLifetimeId`: 12 raw bytes exactly as carried in `IpcHelloMessage`'s
  new field (4-byte little-endian PID, then 8-byte little-endian
  `FILETIME`-valued creation timestamp) -- see "Lifetime-scoped rendezvous and
  shutdown identity" below.
- HMAC message = `challenge (32) || correlationId (8) || adapterInstanceId
  (16) || ownerLifetimeId (12)`, concatenated in exactly this field order:
  68 bytes total.
- `hostProof`/`expectedProof`: the full, untruncated 32-byte HMAC-SHA256
  output.

Known-answer test vector (shared verbatim by both languages' test suites):

```
key (peerProofToken)   = 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f
challenge               = 202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f
correlationId           = 1                     (encoded: 0100000000000000)
adapterInstanceId       = 404142434445464748494a4b4c4d4e4f
ownerLifetimeId         = 505152535455565758595a5b
message (68 bytes)      = 202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f
                          0100000000000000
                          404142434445464748494a4b4c4d4e4f
                          505152535455565758595a5b
hostProof (HMAC-SHA256) = 680480a058a61903835d6f63a3e0e92e5ef4f36ede56aad567bde6b305059deb
```

(Computed with Python's `hmac`/`hashlib` standard library as the reference
implementation; both the C++ and C# known-answer tests assert this exact
`hostProof` for this exact input.)

### Lifetime-scoped rendezvous and shutdown identity

The startup rendezvous and shutdown-request mechanisms are host/adapter
implementation detail, not `protocol/`, but they must never let two
independent, concurrently running Skyrim/adapter lifetimes discover, adopt, or
signal each other's host. A single fixed, global file path or global named
kernel object would not satisfy this -- a second Skyrim process running at the
same time would collide with the first.

- Every adapter/host pairing is scoped by an `ownerLifetimeId`: 12 bytes,
  the owning Skyrim process's OS process id and its `GetProcessTimes` creation
  timestamp. Both values are trivially and deterministically re-derivable by
  any adapter instance running inside the same Skyrim process, and are
  guaranteed to differ from any other Skyrim process,
  including one that later reuses the same PID (its creation time will
  differ). This is not "a PID alone."
- The adapter passes `ownerLifetimeId` to the host it launches via a
  structured process argument (not an environment variable holding secret
  material, and not shell text). The host uses it, for its own process
  lifetime, to: (a) name its rendezvous file and its named shutdown event, and
  (b) validate every incoming Hello's `ownerLifetimeId` before accepting or
  computing `hostProof`, rejecting a mismatch with
  `IpcHelloRejectReason::kLifetimeMismatch`.
- The rendezvous file is a per-user, per-lifetime file (for example
  `%LOCALAPPDATA%\DovahLink\host\rendezvous-<ownerLifetimeId hex>.dat`,
  extending the existing per-user trust-store path convention) containing the
  host's currently bound port and its `peerProofToken`. It is discovery only,
  never authentication: a stale or forged file can only ever name a candidate
  endpoint to attempt, and every candidate -- adopted or freshly launched --
  must still pass the full mutual Hello/HelloAck handshake above, including
  the `ownerLifetimeId` check, before the adapter treats it as its host. A
  stale file naming a dead or unrelated endpoint produces one bounded failed
  connection attempt, never a false adoption.
- The shutdown-request signal is a per-lifetime named Windows event (for
  example `Local\DovahLink.Host.Shutdown.<ownerLifetimeId hex>`), so a
  shutdown request from one Skyrim lifetime's adapter can never reach a
  different lifetime's host.

`ownerLifetimeId` is a collision-avoidance value, not a cryptographic
ownership proof, and must never be described or relied on as one. It is not
secret: any process running as the same Windows user can already read
another process's PID and creation time through public Win32 APIs
(`EnumProcesses`/`GetProcessTimes`), with no special privilege required. Its
only job is preventing two legitimate, non-adversarial concurrent Skyrim
lifetimes from accidentally colliding over the same rendezvous file or
shutdown event name and PID reuse from confusing one lifetime for an older,
dead one -- not defending against a malicious process deliberately forging or
guessing a target lifetime's id. A same-Windows-user process doing that is
explicitly out of scope for this concept, matching the already-approved
`ai/context/protocol/security.md` "Local-OS-user threat boundary" (loopback
TCP and this project's own identifiers are not proof of Windows-user
identity; any connecting process on the same machine under the same account
is already inside this project's accepted trust perimeter unless a future
stage deliberately adds stricter isolation). The channel's actual
cryptographic ownership proof remains exactly what "Mutual authentication"
above describes: the secret, randomly generated `peerProofToken` and the
HMAC-bound `hostProof`/`challenge` exchange. `ownerLifetimeId` only narrows
*which* rendezvous file/event a legitimate adapter looks at; it never
substitutes for that proof.

### Persistent supervision, not one-shot startup

With a configured port of `0`, a host that restarts (crash-and-relaunch) binds
a new, different ephemeral port. The adapter's process-lifecycle supervisor is
therefore not a one-shot startup step: for the whole adapter process lifetime,
it watches for the private connection reporting the host lost (disconnected
without ever re-establishing), and on that signal reruns its bounded
adopt-or-launch-and-verify discovery procedure again, then reconfigures the
existing long-lived connection to the newly discovered endpoint. Each
individual discovery round remains bounded and cancellable, per "Startup
retry" above; only the outer watch-and-retry loop is unbounded for the
process's life, matching the host's own unbounded private-IPC accept loop
(`ai/context/host/architecture.md`'s "adapter reconnect is bounded and
performed outside game-thread work" describes each attempt, not a one-time
budget for the whole session).

The existing `AdapterIpcConnection`/`AdapterIpcSession` pair (Concept 03) is
never stopped and restarted to pick up a new endpoint: `AdapterIpcConnection`
was built so `Stop()` permanently ends it and `AttachConnection` is called
exactly once, so recreating either object mid-session is not this concept's
mechanism. Instead, the connection's own target (its socket's port and the
peer-proof value it presents) is updated in place, safely, while the
connection keeps running and keeps retrying on its own bounded backoff; the
next reconnect attempt inside its already-running retry loop picks up the
updated target. This is the chosen resolution to the two options Concept 04
was asked to weigh: update the live target rather than mint a new
session/connection generation.

#### Discovery-round retry state machine

The supervisor's loop is not "wait for an external disconnect signal, then
run a round": a round can fail without any prior successful connection ever
having existed (for example the very first launch attempt fails, or the host
never becomes reachable), so there is no guarantee a "connection lost" event
will ever arrive to wake a supervisor that is only waiting on that signal.
The loop is instead, precisely:

```
loop until RequestStop():
    run one bounded discovery round (adopt-or-launch, then verify)
    if the round succeeded:
        update the live connection's target (SetPort/SetToken)
        wait until EITHER the connection reports the host lost, OR RequestStop()
        # on waking: loop back to the top and run another round
    else (the round exhausted its bounded attempts without success):
        wait a fixed bounded backoff, OR RequestStop()
        # always loops back to the top and tries again -- never depends on an
        # external signal that a failed round may never produce
```

A successful round's wait is unbounded in wall-clock time (there is no
deadline on how long a healthy connection may stay healthy) but is always
interruptible by `RequestStop()`; a failed round's wait is itself bounded (a
fixed retry delay) and equally interruptible. This guarantees the supervisor
always eventually retries after any failure -- repeated crashes, a launch
that fails outright, or a host that only becomes reachable later -- without
ever requiring an external wakeup that might not come, and without ever
spinning without a delay between attempts.

### Shutdown ordering

Orderly shutdown follows one fixed sequence:

1. Mark the supervisor stopping, so no new discovery round (adoption or
   launch) may begin, and interrupt an in-flight round's bounded wait.
2. Stop and join the supervisor's background thread. This stops the
   *discovery* loop only -- it must not close, release, or otherwise dispose
   of the launched host's process handle or Job Object. That handle's
   lifetime is independent of the supervisor's own stop/join and is only
   ever released in step 5, after steps 3-4 below have had their chance.
3. Request graceful host shutdown: signal the per-lifetime named shutdown
   event (non-blocking), then wait, bounded, for the host process this
   adapter instance launched to actually exit on its own (checked via its
   retained process handle). This step is a no-op wait (nothing to wait on)
   when this instance only adopted an existing host without ever launching
   one itself -- there is no live process handle to wait on or force-close in
   that case, only the signal.
4. If the bounded wait in step 3 elapses without the host exiting, and this
   instance holds a live process handle/Job Object for it (i.e. it was this
   instance that launched it), force-terminate it now (via the Job Object)
   as the deliberate fallback -- not as an accidental side effect of some
   earlier step closing the last handle to the Job Object before the graceful
   attempt was even given a chance.
5. Stop and join the adapter IPC connection, then destroy dependent objects
   (including finally releasing the process handle/Job Object) in the
   reverse of their construction order.

`DllMain`'s `DLL_PROCESS_DETACH` -- the only unload-adjacent hook a classic
SKSE plugin has, since no engine message signals "Skyrim is closing" -- may
only fire step 3's non-blocking *signal* (never the bounded wait that follows
it in the same step, and never steps 1, 2, 4, or 5). It must never join a
thread, wait on a handle, or perform any other blocking I/O: `DLL_PROCESS_DETACH`
runs under the loader lock, where a join or wait can deadlock or hang past the
OS's own patience for process exit. The adapter therefore uses the Skyrim
process lifetime as its supported module lifetime: live plugin unload/reload
while Skyrim remains running is not supported. Worker-owning runtime objects
are intentionally kept alive until the operating system tears down the Skyrim
process, and the launched host's Job Object remains the forced-cleanup
fallback.

The full ordered sequence (steps 1 through 5, including the bounded wait and
forced-fallback) remains one explicit, directly callable, directly testable
orchestration method, entirely separate from the signal-only call `DllMain` is
allowed to make. It is retained for a future safe lifecycle caller and for
focused ordering tests; it is not invoked by DLL detach in the current
process-lifetime policy.

**Process-lifetime policy, recorded explicitly:** there is no confirmed safe
production hook to call the full orchestration method before the process
disappears, since classic SKSE exposes no pre-exit main-thread message and
`ExitProcess`-driven teardown reclaims every adapter-side thread, socket, and
handle regardless of whether that method ran. `DllMain` performs only the
narrow, non-blocking signal from step 3, and worker-owning adapter state is
not destructed as part of the supported process-exit path. A future safe
pre-exit hook may wire the full method without changing its contract, but
discovering or adding such a hook is outside this concept.

## Required handoff from Concept 03

Concept 03 intentionally leaves the plugin's port and proof-token values
provisional. This concept must replace those placeholders with the packaged-host
startup rendezvous and must not leave a production composition path connecting
to port `0` or using an empty proof token. Concept 03 also intentionally leaves
adapter-side authentication gating for host-directed intents incomplete: its
development handlers may marshal `ListenEvent` and `ReadSample` requests without
an accepted host-authentication result. Concept 04 must establish the host proof
and gate both request types before any request can reach game-thread dispatch,
with tests covering pre-handshake and rejected-peer requests.

## Invariants

- No orphaned host may remain after forced Skyrim termination within the
  operating-system supervision guarantee.
- Host failure never blocks or crashes the adapter.
- Host and adapter retain independent process and identity lifetimes.
- Process IDs are diagnostics only and never become DovahLink identity; a
  PID is never treated as identity by itself anywhere in the process-lifecycle
  mechanisms above -- always paired with the owning process's creation
  timestamp as `ownerLifetimeId`.
- `accepted = true` alone is never sufficient to treat a connection as the
  authenticated host, and a verifying `hostProof` never overrides
  `accepted = false`; both are required, per "Mutual authentication"'s exact
  conjunction.
- `ownerLifetimeId` is never described or relied on as a cryptographic
  ownership proof; it only scopes discovery/signaling namespaces. See
  "Lifetime-scoped rendezvous and shutdown identity."
- Two concurrently running Skyrim/adapter lifetimes never observe, adopt, or
  shut down each other's host process.
- A launched host's process handle/Job Object is never released before the
  graceful shutdown request has had its bounded chance to succeed; forced
  termination is a deliberate fallback action, never an accidental
  side effect of an earlier teardown step.
- The supported adapter module lifetime is the owning Skyrim process lifetime;
  live plugin unload/reload while Skyrim remains running is not supported.
- A failed plugin load constructs no thread-owning runtime object before its
  failure guards return.

## Allowed files/modules

- `adapter/include/` and `adapter/src/` — process launch, adoption proof,
  supervision, startup retry, and shutdown modules only. (This repository's
  actual layout uses per-concern module directories rather than a literal
  `include`/`src` split; the equivalent scope is a new `adapter/process/`
  module plus the touches below.)
- `adapter/ipc/` — required, beyond the above: `IpcHelloMessage`/
  `IpcHelloAckMessage`/`ipc_enums.hpp` for the D2 wire-contract change, the
  frame codec, `adapter_ipc_session.cpp`'s handshake verification and its
  mandatory ListenEvent/ReadSample gating, and a new settable peer-proof
  provider used only by the real plugin composition (the existing fixed one
  remains for test fixtures).
- `host/DovahLink.Host/Process/` and `host/DovahLink.Host/Program.cs` — host
  startup/composition and deterministic shutdown integration only.
- `host/DovahLink.Host/Adapter/Ipc/` — required, beyond the above: the
  matching C# Hello/HelloAck/Enums changes for D2, `AdapterIpcSession.cs`'s
  `hostProof` computation and `ownerLifetimeId` validation, and
  `AdapterPeerProofVerifier`.
- Concept 01's golden vectors (wherever each side's contract tests keep them)
  must be regenerated to match the D2 field additions.
- `adapter/tests/`, `host/DovahLink.Host.Tests/Process/`, and a narrowly scoped
  cross-process harness module only if required to prove the contract.
- No client WebSocket, pairing, public protocol cutover, or `bridge/` files.

## Proof obligations

- Hidden launch and safe adoption are tested with controllable process doubles.
- Dynamic-port startup is tested with configuration `0`, including successful
  host binding, exact adapter discovery of the assigned port, peer-proof
  validation during rendezvous, and bounded failure when endpoint handoff is
  unavailable. A configured nonzero port remains supported and fails clearly
  when already occupied.
- Startup retry is bounded, cancellable, and never runs on a game-thread path.
- Graceful close requests host teardown and handles an already-dead host.
- Forced owner termination is covered by supervision/cleanup tests.
- The host remains valid without an adapter and reports controlled unavailability.
- Pre-handshake and rejected-peer event/sample intents cannot reach the adapter's
  game-thread dispatcher.
- Papyrus-facing connection/status behavior reports host readiness or “host not
  ready” without blocking Skyrim or inventing a local fallback state.
- A fake listener that returns `accepted = true` with a missing, forged, wrong,
  or replayed `hostProof` is never treated as an authenticated host.
- A stale rendezvous record (dead port, or a port some other process now
  occupies) produces one bounded failed attempt and a fallback to fresh
  launch, never a false adoption.
- Two concurrently running Skyrim/adapter lifetimes (distinct
  `ownerLifetimeId` values) cannot adopt or shut down each other's host, even
  when both run on the same machine at the same time.
- A host crash followed by a restart on a new dynamic port is detected by the
  running adapter, rediscovered, reverified, and reconnected without adapter
  restart.
- Repeated host crashes, a discovery round that fails outright (no host ever
  reachable), and a later recovery are each covered: a failed round always
  self-schedules its own bounded retry rather than waiting indefinitely for
  an external signal that may never arrive.
- A shutdown request racing against an in-flight discovery round or a pending
  reconnect interrupts that round/attempt and starts no new one; no host
  relaunch is ever initiated once shutdown has begun. Covered for every wait
  state the supervisor can be in: mid-round, waiting after a successful
  round, and waiting after a failed round's backoff.
- The graceful-shutdown orchestration method preserves the Job Object/process
  handle through the signal-and-bounded-wait step and only force-terminates
  as an explicit fallback afterward, never before.
- A structural check confirms `DllMain`'s `DLL_PROCESS_DETACH` calls only the
  non-blocking shutdown-signal method, never the full orchestration method or
  any blocking wait/join.

## Non-goals

- Packaging the final Vortex release, deleting `bridge/`, or final runtime
  conformance; those belong to later stages.
- Adding application behavior to the adapter.
- A safe live plugin unload/reload lifecycle or a confirmed, blocking
  pre-exit hook for the full ordered shutdown sequence; production uses only
  the non-blocking `DllMain` signal and OS process teardown, per "Shutdown
  ordering" above.

## Completion evidence

- Focused lifecycle/composition checks pass.
- The phase completion gate can point to an independently buildable adapter,
  host, and private-channel proof without unresolved divergence.
- The real Windows process checks launch the actual C# host, verify dynamic
  rendezvous and mutual authentication, prove cross-lifetime isolation,
  validate graceful signal shutdown, prove Job Object cleanup after abrupt
  owner termination, and reconnect the running adapter IPC connection after
  a host restart on a new dynamic port.
