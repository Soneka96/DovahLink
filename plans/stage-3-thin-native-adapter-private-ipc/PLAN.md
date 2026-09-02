# Stage 3 — Thin Native Adapter and Private IPC

## Source

- Source path: `host/PLAN.md`
- Phase: Stage 3 — Thin Native Adapter and Private IPC
- Snapshot date: 2026-08-29
- Source fingerprint: `host/PLAN.md` — `4CFA216D1121044DCB046527AF7ED5A930300EC00934432709DDA575E2020584`
- Current source matches this fingerprint: yes

## Objective and boundaries

This package preserves the Stage 3 source scope verbatim:

> **Scope:**
> Create the new C++ SKSE adapter and the private host-to-adapter channel. Keep
> game-thread work bounded and synchronous where Skyrim requires it, while
> moving all client-facing behavior out of the native process. The adapter must
> remain safe when the host is unavailable, restarting, or shutting down.

> **Acceptance criteria:**
>
> - The adapter builds independently from the old `bridge/` implementation.
> - Skyrim/CommonLib headers and runtime objects do not cross into host code.
> - Game-thread callbacks perform only approved reads, validation, owned capture,
>   and bounded handoff.
> - The IPC channel uses owned messages, explicit limits, cancellation, and
>   deterministic shutdown.
> - Host loss cannot block or crash Skyrim capture.
> - Adapter loss is observable by the host and results in a controlled
>   unavailable/resynchronization state.
> - The adapter can answer a host resynchronization request through an approved
>   game-thread path.
> - The adapter starts or safely adopts only the host process belonging to its
>   Skyrim lifetime, launches it without a visible window, retries without
>   blocking game-thread work, and cleans it up after orderly or forced Skyrim
>   termination.

> **Not in scope:** client WebSockets, pairing policy, or public protocol
> cutover.

> **Depends on:** Stage 1 and Stage 2

> **Notes:** A small native handoff queue is expected; moving WebSocket queues to
> the host does not permit blocking a Skyrim callback on IPC availability.

The ownership target is unchanged: `host/` owns application behavior and the
public client boundary; `adapter/` owns only the Skyrim/CommonLib boundary,
native lifecycle, bounded capture handoff, and private IPC. `bridge/` remains a
frozen reference and is not modified.

## Inherited invariants

- The adapter is an out-of-process native SKSE plugin and never embeds the CLR,
  loads host assemblies, or passes Skyrim/CommonLib runtime objects to the host.
- The private IPC contract is separate from the public SDK-to-host contract and
  must fail closed on malformed input, unauthorized peer, or exceeded limits.
- Host and adapter are distributed as one atomic package. Private IPC does not
  negotiate a protocol version; peer ownership and Skyrim-lifetime proof are the
  compatibility boundary.
- The host is the listening/owning side of private IPC; the adapter connects.
- Adapter identity, host process lifetime, play-context identity, and transport
  connection generation remain independent lifetimes.
- Host loss leaves Skyrim capture safe and non-blocking. A bounded native handoff
  may retain only owned captured values; it must not grow without limit.
- No worker may perform a deferred Skyrim runtime read. A game-thread callback
  reads and validates the required runtime data, then hands off owned data.
- The adapter contains no pairing, trust, authorization, subscription, cadence,
  public-protocol, client-session, queue-policy, or application-state logic.
- Host-directed event/sample intent is represented as a small host-owned key or
  token. The adapter only maps that key at the final Skyrim boundary to an
  approved event registration or read function.
- Papyrus is a thin Skyrim-facing command/status surface only. It forwards
  commands, reports host readiness/unavailability, and formats approved host
  results; it owns no retry, pairing, or business policy.
- Shutdown is deterministic and idempotent. In-flight callbacks and transport
  completions are contained or rejected by lifetime/generation guards.
- Tests use plain values, controllable thread-safe fakes, deterministic queues,
  and structural checks; real Skyrim validation is reserved for runtime-only
  behavior and must be recorded separately.
- New handwritten C# and C++ behavior-bearing types have explicit contracts,
  constructor/injected collaborators where applicable, and complete local
  documentation under the area conventions.

## Requirement IDs

- **R1** — “The adapter builds independently from the old `bridge/` implementation.”
- **R2** — “Skyrim/CommonLib headers and runtime objects do not cross into host code.”
- **R3** — “Game-thread callbacks perform only approved reads, validation, owned capture, and bounded handoff.”
- **R4** — “The IPC channel uses owned messages, explicit limits, cancellation, and deterministic shutdown.”
- **R5** — “Host loss cannot block or crash Skyrim capture.”
- **R6** — “Adapter loss is observable by the host and results in a controlled unavailable/resynchronization state.”
- **R7** — “The adapter can answer a host resynchronization request through an approved game-thread path.”
- **R8** — “The adapter starts or safely adopts only the host process belonging to its Skyrim lifetime, launches it without a visible window, retries without blocking game-thread work, and cleans it up after orderly or forced Skyrim termination.”

## Concept graph

1. **Private IPC contract and message model** (`01-private-ipc-contract.md`)
   establishes the private frame/message vocabulary, limits, peer proof,
   cancellation, and ownership rules, including opaque event/sample intents. It
   has no dependency on the old bridge and is the foundation for both process
   sides.
2. **Host IPC channel and recovery** (`02-host-ipc-channel.md`) implements the
   C# listening side, connection generation, loss observation, and controlled
   resynchronization using the existing host availability seam.
3. **Native adapter boundary and handoff** (`03-native-adapter-core.md`)
   implements the independent C++ adapter, game-thread-only runtime reads,
   thin key-to-Skyrim mapping, bounded owned capture handoff, and IPC client.
4. **Process lifecycle and composition** (`04-process-lifecycle.md`) connects
   adapter startup to hidden host launch/adoption, bounded retry, parent-lifetime
   supervision, graceful shutdown, and forced cleanup.

The order moves from the cross-process contract to host behavior, then to the
native boundary, and finally to process composition. Concepts 2 and 3 are
independent consumers of concept 1, but are executed serially for review.

## Traceability matrix

| Requirement | Concept(s) | Status | Mapping |
|---|---|---|---|
| R1 | 03, 04 | preserved | Independent adapter build and no `bridge/` dependency |
| R2 | 01, 02, 03 | preserved | Private DTO/frame boundary and native-only Skyrim types |
| R3 | 03 | decomposed | Callback capture and final key-to-runtime mapping |
| R4 | 01, 02, 03, 04 | decomposed | Contract, host listener, adapter client, and teardown |
| R5 | 03, 04 | decomposed | Non-blocking bounded handoff and host-loss-safe lifecycle |
| R6 | 02 | preserved | Host observes disconnect and requires fresh resynchronization |
| R7 | 02, 03 | decomposed | Host request plus approved game-thread capture path |
| R8 | 04 | preserved | Hidden launch/adoption, retry, supervision, and cleanup |

## Phase completion gate

- All concepts are complete and their focused tests pass.
- The C# host and C++ adapter build independently without a production
  dependency on `bridge/`.
- Static/structural checks prove Skyrim/CommonLib includes and runtime objects
  are confined to adapter code.
- Contract tests prove framing, size limits, cancellation, peer rejection,
  backpressure, disconnect, reconnect, and deterministic close.
- Native tests prove callbacks perform bounded synchronous reads and enqueue only
  owned values; no worker performs deferred runtime reads.
- Host-loss and adapter-loss tests prove safe capture, unavailable state, and
  fresh baseline resynchronization.
- Lifecycle tests prove hidden launch, safe adoption proof, bounded startup retry,
  graceful Skyrim-close teardown, crash/forced-termination cleanup, and no
  orphaned host process.
- No client WebSocket, pairing, public protocol cutover, or `bridge/` deletion
  has been introduced.
- CodeGraph is rerun against the final changed area, focused tests and relevant
  build checks pass, and the working-tree diff contains no unrelated changes.

## Divergence policy

See `DIVERGENCES.md` for the approved removal of private protocol-version
negotiation from the Stage 1 design record. The thin-adapter ownership
clarification and concept ordering preserve the Stage 3 source acceptance
criteria.
