# 03 — Native adapter boundary and bounded handoff

Status: complete

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

## Scope decision

Concept 03 is complete as the adapter and private-IPC foundation. It deliberately
does not introduce a production Skyrim state domain. The generic dispatch seam,
game-thread marshalling, owned handoff, bounded queue, and adapter-side connection
are implemented and tested; concrete Skyrim mappings, capture delivery, and an
authoritative resynchronization baseline belong to Stage 6.

The first Stage 6 state slice is intentionally narrow:

- one native level-up event;
- one fast sample containing health, magicka, and stamina together;
- one medium sample for experience/XP;
- one slow sample for gold/coins.

The host owns the meaning and cadence of fast, medium, and slow. The adapter
receives only opaque event keys or sample tokens and performs the final native
registration or read for those tokens.

## Contracts

- A host key/token reaches a small adapter-owned dispatch seam. Concrete event
  registrations and synchronous reads are added with the approved Stage 6 state
  slice, not invented in this foundation concept.
- When a concrete mapping exists, the dispatch entry is the last translation
  step before Skyrim: it converts a host-supplied event key or sample token into
  the approved native event/read operation, with no adapter-side subscription,
  cadence, or domain policy.
- Callback work is synchronous only where Skyrim requires it: validate, copy the
  necessary value into an owned item, and perform a bounded non-blocking handoff.
- Worker/IPC code consumes owned values only and never calls back into Skyrim.
- Full/disconnected handoff drops or replaces according to the message class;
  it never waits for host availability.
- Papyrus reports “host not ready” (or an equivalent explicit unavailable
  status) when the host channel is not ready. Skyrim-facing command forwarding
  remains deferred until a concrete command is approved; the status surface
  does not implement retries or policy.

## Invariants

- The adapter contains no pairing, trust, public protocol, subscription,
  cadence, queue-policy, or application-state decisions.
- CommonLib headers and runtime objects remain confined to adapter code.
- Host loss cannot block or crash callbacks; adapter reconnect is bounded and
  performed outside game-thread work.
- The adapter never fabricates a resynchronization baseline; until Stage 6 adds
  an approved baseline provider, it reports that no baseline is available.
- Future event registration and sample reads are selected by the final thin
  dispatch mapping, not by complex adapter logic.

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
- Callback tests prove game-thread marshalling, owned capture handoff, bounded
  handoff, and no deferred runtime read through injected fakes. Proof of real
  Skyrim reads and registrations belongs to Stage 6 runtime tests.
- Queue-full, host-loss, cancellation, reconnect, and shutdown paths do not
  block or crash the callback.
- Event/sample key mapping remains a thin dispatch seam; concrete mappings and
  state policy stay in the Stage 6 host/adapter tests rather than becoming
  adapter-side cadence policy.
- Papyrus status tests prove that an unavailable host is reported explicitly and
  does not cause an unbounded or blocking connection attempt. Command forwarding
  is deferred with the first approved command surface.

## Non-goals

- Generic event frameworks, speculative domain registries, client behavior,
  pairing/admin decisions, or public message serialization.
- The first production state flow, outbound capture messages, and accepted
  resynchronization baseline; those belong to Stage 6.
- Host process launch and OS parent-lifetime supervision; those belong to concept
  04.

## Deferred work

- Concept 04 replaces the provisional adapter endpoint and proof-token wiring
  with the packaged host launch, startup rendezvous, bounded retry, and
  parent-lifetime supervision required for an operational private channel.
- Stage 6 adds the four-state first slice listed above, sends owned captures to
  the host, marks accepted resynchronization data as a fresh authoritative
  baseline, and proves the behavior with adapter/host boundary tests and the
  approved runtime checks.

## Completion evidence

- Adapter unit and structural tests pass.
- Independent native build passes with only the approved toolchain and required
  dependencies.
