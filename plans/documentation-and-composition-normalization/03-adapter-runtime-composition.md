# Concept 03 -- Adapter runtime composition and process-lifetime ownership

**Status:** pending

**Covers:** R3.1-R3.10 (see `PLAN.md` Requirement IDs; original wording in `SOURCE.md`
Block A Issue 3).

**Depends on:** Concept 01.1 merged to `main` (`ai/context/skse/cpp-style.md` must
already be free of normative Bridge-genealogy dependencies, per D1/R1.14, before this
concept's design work and documentation are written against it; the enum/constants
physical normalization 01.1 performs must also be in place first, so this concept's
`AdapterRuntime` composition work reads from the already-normalized `adapter/enums.hpp`
and `adapter/constants.hpp` rather than the pre-normalization per-module layout).

## Why this is a stable concept

The Adapter's process-lifetime object graph, currently assembled inline in
`SKSEPluginLoad`, is one ownership boundary distinct from anything Host-side: no
shared files with Concept 02, and it must land before Concept 05 documents the result.
The safety constraint this concept must not disturb -- thread-owning destructors must
not block during `DLL_PROCESS_DETACH` under the Windows loader lock -- is the single
invariant that makes this a careful, self-contained unit rather than an ordinary
refactor.

## Design

- Introduce one explicit `AdapterRuntime` (or equivalently named) type owning/
  referencing the actual current long-lived Adapter graph (capture handoff queue, task
  marshaller, native dispatcher, pairing notification sink, adapter identity, IPC
  session, socket, frame codec, rendezvous reader, Host process launcher, IPC
  connection, Host supervisor -- using the real current types, not the illustrative
  list verbatim).
- Where a narrow `AdapterStartupContext` genuinely clarifies a startup-only value
  contract (resolved paths, runtime data), introduce it -- not as a catch-all parameter
  bag.
- Reshape `SKSEPluginLoad` into: validate SKSE environment -> resolve startup values ->
  construct one `AdapterRuntime` -> wire/register framework boundaries -> start the
  runtime at the correct SKSE lifecycle event. The detailed object graph moves into
  `AdapterRuntime`'s own construction, not into a second free function that just
  relocates the same inline code.
- No `AdapterRuntime::Instance()` or any other globally reachable accessor. Ordinary
  services keep receiving dependencies through their constructors.
- Preserve the existing process-lifetime allocation strategy exactly. Do not introduce
  destruction for anything that currently intentionally outlives normal shutdown --
  this concept clarifies ownership, it does not change what is destroyed or when.
- Keep `DllMain` minimal: it may signal Host shutdown, never join/wait/destroy
  worker-owning services.
- Document the loader-lock rationale concisely on `AdapterRuntime` itself (2-4 lines,
  current-state only, no migration history) rather than leaving it implicit or spread
  across comments.

## Files this concept may change

- `adapter/plugin/dovahlink_adapter_plugin.cpp` and its `SKSEPluginLoad`
- New `AdapterRuntime` / `AdapterStartupContext` header(s)/source file(s) under
  `adapter/plugin/` or another directory matching the current module layout
- `DllMain` and its owning file, if not already `dovahlink_adapter_plugin.cpp`
- Adapter test files proving the composition/lifecycle contracts below

## Tests / proof obligations

Per R3.9, add/adjust tests for --

- composition wiring producing the expected object graph;
- exactly one process-lifetime `AdapterRuntime` constructed by the plugin composition
  path;
- callbacks routed to the correct process-lifetime services;
- startup occurring at the expected SKSE lifecycle stage;
- shutdown signaling remaining idempotent;
- no runtime object being destroyed through an unsafe DLL-detach path.

Do not fabricate a live DLL unload/reload contract that DovahLink does not support --
test the signaling and construction guarantees that actually exist.

## Non-goals

- Documentation normalization of the resulting Adapter code (Concept 05, after this
  merges).
- Any C++ DI framework or container.
- Any change to feature/protocol/runtime behavior.
- Host-side composition (Concept 02) -- no shared files.
- "Cleaning up" the intentional process-lifetime leak.

## Completion criteria and evidence

- Every R3.x acceptance bullet satisfied and traceable to a specific file/test.
- Full Adapter/C++ test suite green; new composition/lifecycle tests included and
  passing.
- `PLAN.md` status table updated with this concept's PR number, marked `Complete` once
  merged; unblocks Concept 05.
