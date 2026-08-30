# 03 — Native adapter boundary and bounded handoff

Status: pending

Covers: R1, R2, R3, R4, R5, R7

Depends on: `01-private-ipc-contract.md`; the approved CommonLib/SKSE toolchain.

## Owner and boundary

The adapter owns only SKSE loading, native lifecycle callbacks, approved
game-thread reads, thin key-to-runtime mapping, owned capture, and the private
IPC client. It is intentionally not an application layer. The host chooses the
event/sample intent; the adapter performs only the final native registration or
read needed to satisfy that intent.

## Inputs and outputs

Inputs are host-directed event/sample keys or tokens, SKSE callbacks, and
approved Skyrim runtime objects visible only at the callback boundary. Outputs
are owned bounded captures, lifecycle notifications, status, and private IPC
responses.

## Contracts

- A host key/token maps to a small adapter-owned dispatch entry that reaches one
  approved event registration or synchronous read function.
- The dispatch entry is the last translation step before Skyrim: it converts a
  host-supplied event key or sample token into the approved native event/read
  operation, with no adapter-side subscription, cadence, or domain policy.
- Callback work is synchronous only where Skyrim requires it: validate, copy the
  necessary value into an owned item, and perform a bounded non-blocking handoff.
- Worker/IPC code consumes owned values only and never calls back into Skyrim.
- Full/disconnected handoff drops or replaces according to the message class;
  it never waits for host availability.
- Papyrus forwards Skyrim-facing commands, requests the existing connection
  path when needed, and reports “host not ready” (or an equivalent explicit
  unavailable status) when the host channel is not ready. It formats
  host-approved values only and does not implement retries or policy.

## Invariants

- The adapter contains no pairing, trust, public protocol, subscription,
  cadence, queue-policy, or application-state decisions.
- CommonLib headers and runtime objects remain confined to adapter code.
- Host loss cannot block or crash callbacks; adapter reconnect is bounded and
  performed outside game-thread work.
- Resynchronization capture is obtained through an approved game-thread path and
  is marked as a fresh baseline.
- Event registration and sample reads are selected by the final thin dispatch
  mapping, not by complex adapter logic.

## Allowed files/modules

- `adapter/CMakeLists.txt`, `adapter/CMakePresets.json`,
  `adapter/vcpkg.json`, and `adapter/vcpkg-configuration.json` — independent
  native build definition, only where required by the approved toolchain.
- `adapter/include/` and `adapter/src/` — adapter IPC client, bounded handoff,
  native dispatch, capture, SKSE lifecycle, and thin Papyrus-facing status
  modules.
- `adapter/tests/` — adapter unit and structural tests.
- No `bridge/`, public protocol, SDK, app, or host application-service files.

## Proof obligations

- Independent adapter configuration/build does not link or include `bridge/`.
- Structural checks prove no Skyrim/CommonLib include or type reaches host code.
- Callback tests prove synchronous read, owned capture, bounded handoff, and no
  deferred runtime read.
- Queue-full, host-loss, cancellation, reconnect, and shutdown paths do not
  block or crash the callback.
- Event/sample key mapping remains a thin dispatch table or equivalent seam;
  policy stays in host tests, not adapter code.
- Papyrus command/status tests prove that an unavailable host is reported
  explicitly and does not cause an unbounded or blocking connection attempt.

## Non-goals

- Generic event frameworks, speculative domain registries, client behavior,
  pairing/admin decisions, or public message serialization.
- Host process launch and OS parent-lifetime supervision; those belong to concept
  04.

## Completion evidence

- Adapter unit and structural tests pass.
- Independent native build passes with only the approved toolchain and required
  dependencies.
