# Source snapshot

This file is the frozen source specification for the
`documentation-and-composition-normalization` phase package. It exists because this
initiative is cross-cutting engineering hygiene with no product-roadmap phase or
committed `PLAN.md` behind it, and `gh` was unavailable in-session to confirm or create
matching GitHub issues. The text below is preserved verbatim, in three dated blocks,
exactly as authored in chat on 2026-09-13: the original five issue drafts, the
maintainer's approved refinement after reviewing a repository-grounded analysis of
them, and the maintainer's second approved refinement covering the execution contract,
`[Unreleased]` backfill, and package-wide invariants.

**Source baseline:** `main` @ `499bd4f44e93e870388ff54f4f91489e3c546064` (PR #58 merged),
2026-09-13.

**Immutability:** This file becomes immutable once the phase package built from it is
approved. A later change in scope, priority, or interpretation is recorded as a
`DIVERGENCES.md` entry or a `PLAN.md`/`CONTEXT.md` decision -- never as an edit to the
blocks below. If the maintainer deliberately re-opens and amends the source itself, add
a new dated block rather than editing an existing one, and note the re-fingerprint in
`PLAN.md`'s Source section.

**Fingerprint:** SHA-256 content hash of this file, recorded in `PLAN.md`'s Source
section (computed over the final content, after all three blocks below were added).

Requirement IDs (`R1.1`-`R5.10`) are defined in `PLAN.md`, each pointing back to a
specific bullet or section in this file rather than re-quoting it. Concept files cite
requirement IDs and a short paraphrase; this file remains the one verbatim copy.

## Block A -- original issue drafts (pasted 2026-09-13)

# Issue 1 — Normalize documentation conventions and add an Unreleased changelog workflow

## Goal

Normalize DovahLink's documentation and changelog conventions **before touching the existing codebase**, so future work does not keep recreating the current readability problems.

DovahLink should retain its unusually high documentation coverage. The problem is **verbosity, placement, and mixing unrelated information**, not that too much of the code is documented.

This task must not change runtime behavior.

## Documentation philosophy

Every handwritten declaration should remain documented, including:

* classes;
* interfaces;
* structs/records;
* enums and enum values;
* constructors;
* fields;
* properties;
* methods;
* parameters;
* return values;
* exceptions where relevant;
* private helpers;
* test helpers.

Keep useful XML/Doxygen structure such as:

* `<summary>`;
* `<param>`;
* `<typeparam>`;
* `<returns>`;
* `<exception>`;
* `<inheritdoc/>`;
* `@param`;
* `@return`;
* `@throws`;
* `@copydoc`.

The goal is:

> **High documentation coverage, low documentation verbosity.**

## Documentation size and readability

Documentation should use the smallest amount of text required to clearly explain the declaration's **current purpose and contract**.

Normal expectations:

* field/property/enum member: usually 1 line;
* simple parameter: usually 1 line, rarely more than 2-3;
* return value: usually 1-2 lines;
* exception: usually 1-2 lines;
* constructor summary: usually 1-3 lines;
* method summary: usually a few lines;
* class/interface summary: normally a small concise block.

These are readability guidelines, not hard mechanical limits.

Longer documentation is allowed when a real security, concurrency, ownership, protocol, lifecycle, or failure contract genuinely requires it.

However, documentation approaching 20-40 lines for a normal method or parameter should be treated as a strong signal that information belongs somewhere else.

## What declaration documentation should contain

Documentation should describe things such as:

* current purpose;
* inputs;
* result semantics;
* important preconditions;
* failure conditions;
* ownership;
* lifetime;
* concurrency guarantees;
* security requirements;
* externally observable side effects.

## What declaration documentation should NOT contain

Do not normally include:

* history of previous implementations;
* PR/reviewer discussions;
* "before this change";
* "the previous implementation";
* "the earlier fix";
* migration genealogy;
* roadmap-stage narration;
* "later concept";
* speculative future implementation plans;
* explanations of every internal algorithm step;
* references to legacy code merely because the current implementation originated there.

History is acceptable only when it remains part of the **current compatibility or safety contract**.

## Method bodies

Method bodies should primarily contain code.

Do not put long documentation blocks or algorithm narration inside methods.

Allow short implementation comments only when they explain non-obvious **why**, for example:

* lock/synchronization ordering;
* ownership/lifetime requirements;
* security-sensitive ordering;
* SKSE requirements;
* Windows/platform quirks;
* loader-lock constraints;
* non-obvious workaround;
* an operation that intentionally looks unusual.

Such comments should normally remain around 1-3 lines.

Good:

```csharp
// Read outside the subscription lock to avoid lock-order inversion.
var snapshot = feed.GetSnapshot(...);
```

Bad:

```text
10-20 lines explaining the entire implementation,
what the previous version did,
which PR introduced it,
and what a future roadmap phase may replace it with.
```

Do not comment obvious code.

## Information ownership

Use this separation:

| Information                          | Location                             |
| ------------------------------------ | ------------------------------------ |
| Current declaration contract         | XML/Doxygen                          |
| Short local implementation reason    | Inline comment                       |
| Cross-component/state-machine design | Architecture/design docs             |
| Future work                          | Roadmap / GitHub issue / narrow TODO |
| Unreleased behavior change           | `[Unreleased]` changelog             |
| Released change                      | Versioned changelog                  |
| Implementation history               | Git / PR history                     |

Do not introduce a generic `TODO.md` that competes with the roadmap/issues.

## Member ordering

Remove any normal-class convention requiring newly added members to simply be appended to the end for line-number stability.

Normal classes should be organized semantically.

Suggested C# order:

1. constants/static state;
2. injected dependencies;
3. mutable instance state;
4. constructors;
5. properties/events;
6. interface/override methods;
7. other public/internal methods;
8. private helpers;
9. nested types.

Interface/override methods should stay grouped together rather than having private helpers inserted between them.

Preserve order-sensitive rules only where order is actually semantically significant.

### C++ warning

Do not blindly reorder C++ data members.

Declaration order can affect:

* construction;
* destruction;
* aggregate initialization;
* designated initialization;
* ABI/layout assumptions.

Only reorganize C++ members when behavior and initialization semantics remain correct.

## `[Unreleased]` changelog

Add this above versioned releases:

```markdown
## [Unreleased]

### Added
### Changed
### Fixed
### Removed
### Security
```

Unused empty subsections may be omitted if preferred, but `[Unreleased]` must remain the first release section.

### Feature PR behavior

Any PR containing a notable developer/user-visible change should update `[Unreleased]`.

A changelog bullet describes **what changed**, not how it was implemented.

Prefer:

```markdown
- Added reserved control and data outbound lanes so state publication cannot starve control traffic.
```

Avoid:

```markdown
- Added DataLaneOutboundQueue backed by LinkedList nodes, callback-based byte accounting,
  several writer-loop modifications, and changes made after the previous recovery race fix...
```

Rules:

* one outcome per bullet;
* normally one concise sentence;
* no implementation walkthrough;
* no PR history;
* no reviewer history;
* no future roadmap narration;
* split unrelated changes into separate bullets.

If a bullet requires a paragraph, the detailed explanation probably belongs somewhere else.

## Release workflow

When releasing:

1. move/promote `[Unreleased]` entries into:

```markdown
## [x.y.z] - YYYY-MM-DD
```

2. perform the version/package synchronization owned by the release;
3. leave a new empty `[Unreleased]` section at the top.

This allows `main` to accurately record everything already implemented without stuffing implementation history into source comments.

## Existing changelog cleanup

Also fix current drift:

* CHANGELOG should describe **DovahLink**, not only the old DovahLink Bridge;
* reconcile release/version responsibilities with the current roadmap convention;
* remove contradictions between `CHANGELOG.md` and `ai/context/common.md`.

## Scope

Inspect at minimum:

* `ai/context/common.md`
* `ai/context/dotnet/csharp-style.md`
* `ai/context/skse/cpp-style.md`
* `CHANGELOG.md`
* repository consistency tests/tooling that encode affected rules

Only touch other convention files if they inherit or contradict these rules.

## Acceptance criteria

* Documentation coverage is not reduced.
* Concise documentation is explicitly preferred.
* Params/results/XML/Doxygen remain strongly encouraged.
* Long method-body documentation is prohibited/discouraged.
* Current contract, history, architecture and future plans have clearly separated homes.
* Normal class members use semantic ordering instead of append-only organization.
* `[Unreleased]` exists at the top of the changelog.
* Feature and release changelog responsibilities are explicit.
* Changelog bullets remain concise and outcome-focused.
* Relevant convention tests/tooling are updated.
* No runtime behavior changes.

## Ordering

Do after PR #58 is merged.

This issue should be completed before the Host and Adapter documentation sweeps so the new rules become authoritative first.

---

# Issue 2 — Normalize Host composition, DI scopes and service lifetimes

## Goal

Normalize `DovahLink.Host` composition so dependencies and lifetimes are explicit, testable, and difficult to misuse.

The current manual composition is valid, but `Program.cs` now constructs a large portion of the Host graph directly. As the Host continues growing, service ownership and lifetime should become explicit rather than being encoded implicitly in one large composition method.

This is primarily an architecture/composition task. Preserve current behavior.

## Core principle

> DI constructs dependencies. The Host runtime controls lifecycle.

Do not let the DI container silently determine security-sensitive initialization/startup/shutdown order.

## Use .NET DI for composition

Introduce normal `Microsoft.Extensions.DependencyInjection` / Generic Host-style registration where appropriate.

Split registration into cohesive modules rather than building one giant bHealth-style service registration file.

Suggested shape:

```text
Composition/
  HostServiceExtensions.cs
  SecurityServiceExtensions.cs
  AdapterIpcServiceExtensions.cs
  PublicClientServiceExtensions.cs
  StateServiceExtensions.cs
  ProcessServiceExtensions.cs
  ConfigurationServiceExtensions.cs
```

Exact names may change if a better organization emerges.

## Explicit lifetime model

Audit every Host service and classify its lifetime according to the DovahLink domain.

Use these conceptual lifetimes:

### Host-lifetime singleton

Exactly one authoritative instance for the Host process.

Likely examples include services such as:

* trust persistence/state;
* session registry;
* security-state gate;
* pairing coordinator where appropriate;
* play-context tracking;
* registered-state-area policy;
* Host settings snapshot;
* public connection registry;
* stateless codecs;
* trust administration;
* Host-wide publication/state source.

The audit must decide based on responsibility rather than blindly following this list.

### Client-scoped

Exactly one instance per logical trusted client **only where the state genuinely belongs to `clientId` and must survive session/connection replacement**.

Do not invent client-scoped state merely for convenience.

### Session-scoped

Exactly one instance per authenticated DovahLink `sessionId`.

Likely examples include state that must not leak between reconnects, such as:

* state subscription state;
* recovery state associated with one session.

Every collaborator within one session that requests that service should observe the same session-owned instance.

A new session must get a fresh instance.

### Connection-scoped

Exactly one instance per physical WebSocket/transport connection.

Likely examples include:

* connection context;
* connection cancellation/liveness state;
* outbound queues;
* socket-bound resources.

### Transient

Short-lived operation/request objects without a longer ownership requirement.

## Important identity distinction

Do not confuse:

```text
clientId
sessionId
connection
```

A trusted client can survive reconnects.

A session must not accidentally inherit stale transient state from an old session merely because the `clientId` stayed the same.

For example, do not key things such as:

* outbound queue;
* pending socket writes;
* connection cancellation;
* subscription recovery state;

by `clientId` unless the domain contract explicitly requires them to survive reconnects.

## Scoped-singleton behavior

Where something must be unique per session/connection/client, model it as an explicit scoped owner/factory rather than a global static dictionary.

Do not create service-locator-style constructs such as:

```csharp
static Dictionary<string, PublicStateSubscription> Instances;
```

Prefer an explicit session/connection aggregate or factory.

Conceptually:

```text
PublicSession
  ├── one PublicStateSubscription
  ├── session state
  └── connection/session collaborators
```

and/or:

```text
PublicConnection
  ├── one outbound queue
  ├── one connection context
  └── transport-specific state
```

The exact design should follow the current Host boundaries.

## Explicit factories

For non-HTTP scopes, prefer explicit factories/domain owners where this communicates lifetime better than arbitrary DI scopes.

For example:

```text
IPublicSessionFactory
IPublicConnectionFactory
```

These may consume Host-lifetime services from DI and create one coherent session/connection object graph.

Do not force ASP.NET-style request scoping onto a WebSocket/session architecture if it makes ownership less clear.

## Preserve startup/security ordering

This is critical.

The current Host deliberately loads/validates trust/security persistence before exposing externally usable admission/listener paths.

Preserve fail-closed behavior.

Do not accidentally expose listeners before authoritative security state has initialized successfully.

Do not hide asynchronous initialization inside registrations such as:

```csharp
services.AddSingleton(
    TrustStore.CreateAsync(...).GetAwaiter().GetResult());
```

Avoid sync-over-async.

## Explicit Host runtime

Introduce or retain one clear Host lifecycle orchestrator such as:

```text
DovahLinkHostRuntime
```

responsible for the verified lifecycle order.

Conceptually:

```text
build dependency graph
↓
initialize persistent/security state
↓
prepare/start internal Adapter communication
↓
publish readiness/rendezvous at the correct point
↓
expose public client listener/admission
↓
run
↓
ordered shutdown
```

Use the actual existing security/lifecycle contract when implementing the exact order.

Avoid splitting lifecycle across many independently ordered `IHostedService`s where correctness depends on implicit framework ordering.

## Avoid

* service locator;
* static global access to the service provider;
* reflection/assembly-scanning magic;
* sync-over-async constructors;
* duplicate authoritative singleton state;
* accidentally sharing session/connection state across identities;
* DI controlling domain lifecycle implicitly.

## Program.cs

`Program.cs` should become a small composition/bootstrap boundary.

It should not manually instantiate the entire Host graph.

## Tests

Add/adjust tests proving at minimum:

* Host-lifetime services resolve to the same instance;
* different sessions do not share session-owned state;
* collaborators within one session share the intended session-owned service;
* different connections do not share connection-owned state;
* reconnect/new session gets fresh transient/session state;
* `clientId` persistence does not accidentally preserve session/connection state;
* startup still fails closed when trust/security initialization fails;
* listeners/rendezvous are not exposed prematurely;
* shutdown remains idempotent/ordered;
* current test seams remain possible.

## Documentation

Document the lifetime of behavior-bearing services concisely.

Examples:

```csharp
/// <summary>
/// Maintains state subscriptions for one authenticated public session.
/// </summary>
```

or:

```csharp
/// <summary>
/// Stores authoritative trusted-device state for the Host lifetime.
/// </summary>
```

Do not turn lifetime documentation into long implementation essays.

## Acceptance criteria

* `Program.cs` is substantially reduced to bootstrap/composition responsibility.
* Host service registrations are modular and understandable.
* Every behavior-bearing Host dependency has a deliberate lifetime.
* Host/client/session/connection/transient distinctions are explicit.
* No accidental cross-session or cross-connection state sharing.
* Security initialization still fails closed.
* Lifecycle ordering remains explicit rather than delegated to DI magic.
* No service locator.
* No sync-over-async initialization.
* Composition/lifetime tests cover the important boundaries.
* Runtime behavior remains equivalent.

## Dependencies

Do after:

**Normalize documentation conventions and add an Unreleased changelog workflow**

Prefer after PR #58 merges.

---

# Issue 3 — Normalize Adapter runtime composition and process-lifetime ownership

## Goal

Normalize the SKSE Adapter's composition/ownership model without introducing a DI framework into C++.

The existing process-lifetime allocations are intentional because live Adapter DLL unload/reload is unsupported and destroying thread-owning objects during `DLL_PROCESS_DETACH` can be unsafe under the Windows loader lock.

Preserve that safety decision while making ownership and composition substantially clearer.

## Core principle

> The Adapter should have one explicit process-lifetime runtime owner, not a service locator.

Do not introduce a C++ DI container.

Do not introduce globally accessible `Singleton::Instance()` APIs.

## Current problem

`SKSEPluginLoad` currently acts as both:

* SKSE entry point;
* environment validation;
* runtime setup;
* composition root;
* callback wiring;
* process-lifetime object ownership.

It creates a large graph of intentionally process-lifetime objects using function-local static allocations.

The lifetime is reasonable; the composition should become clearer.

## Introduce an Adapter runtime/composition owner

Introduce an object such as:

```text
AdapterRuntime
```

or another appropriately named type representing the process-lifetime Adapter graph.

It should own/reference the long-lived Adapter components needed for the Skyrim process lifetime, such as the existing equivalents of:

* capture handoff queue;
* task marshaller;
* native dispatcher;
* pairing notification sink;
* adapter identity;
* IPC session;
* socket;
* frame codec;
* rendezvous reader;
* Host process launcher;
* IPC connection;
* Host supervisor.

Use the actual current graph rather than mechanically following this example list.

## Startup context

Where helpful, use a narrow:

```text
AdapterStartupContext
```

for startup-only environmental values, not as a catch-all dependency bag.

It may contain values such as resolved paths/runtime data only when those values form a real startup contract.

Do not create a broad `Context` object merely to reduce parameter counts.

Follow the existing C++ parameter-grouping conventions.

## SKSEPluginLoad target shape

Conceptually:

```text
SKSEPluginLoad
  ↓
initialize/validate SKSE environment
  ↓
resolve startup values
  ↓
construct one AdapterRuntime
  ↓
wire/register framework boundaries
  ↓
start runtime at the correct SKSE lifecycle event
```

`SKSEPluginLoad` should remain the SKSE-facing composition boundary, but the detailed graph should no longer dominate the function.

## Process-lifetime behavior

Preserve the existing intentional process-lifetime strategy unless a proven safe destruction boundary exists.

Do not "clean up the leak" merely because static analysis or normal C++ ownership style dislikes it.

The important current invariant is that thread-owning/connection-owning destructors must not block during DLL detach under loader lock.

Document this concisely.

Example:

```cpp
/// Owns the Adapter's process-lifetime services.
///
/// The runtime intentionally outlives normal plugin shutdown because destroying
/// worker-owning services during DLL detach could block under the loader lock.
```

Do not include the complete migration history of how this decision evolved.

## No Singleton service locator

Do not add:

```cpp
AdapterRuntime::Instance()
```

for arbitrary code to fetch dependencies globally.

Ordinary services should continue to receive dependencies explicitly through constructors/interfaces.

The composition boundary owns the one runtime.

The rest of the application does not discover dependencies globally.

## DllMain

Keep `DllMain` extremely small and loader-lock safe.

Preserve the current rule that `DllMain` may signal Host shutdown but must not perform blocking/join/destruction work that could hang under loader lock.

Any existing current rationale around this should remain, but rewritten concisely if currently verbose/history-oriented.

## Lifecycle

Make ownership and startup responsibility clear for:

```text
Skyrim process
    ↓
AdapterRuntime
    ↓
Adapter components
```

Separate:

* process lifetime;
* Host-connection lifetime;
* IPC session state;
* per-operation/game callback data.

Do not accidentally turn short-lived/session-specific state into process-wide singleton state.

## Tests

Add/adjust tests where practical for:

* composition wiring;
* only one process runtime being constructed by the plugin composition path;
* callbacks routed to the correct process-lifetime services;
* startup at the expected SKSE lifecycle stage;
* shutdown signaling remaining idempotent;
* runtime objects not being destroyed through unsafe DLL-detach paths.

Do not fabricate a fake live DLL unload/reload contract that DovahLink does not support.

## Documentation

Follow the normalized repository documentation rules.

Keep concise documentation on every declaration.

Preserve important comments explaining:

* loader lock;
* SKSE callback requirements;
* game-thread requirements;
* process lifetime;
* ownership;
* required operation order.

Remove historical Bridge genealogy where it is not part of the current contract.

## Acceptance criteria

* No C++ DI framework introduced.
* `SKSEPluginLoad` becomes a clearer entry/composition boundary.
* One explicit process-lifetime Adapter runtime owns/groups the long-lived graph.
* No globally accessible service locator/singleton API.
* Constructor injection remains the normal dependency mechanism.
* Loader-lock-safe process-lifetime behavior is preserved.
* `DllMain` remains minimal/non-blocking.
* Shorter, current-state lifecycle documentation exists.
* Relevant composition/lifecycle tests are updated.
* No feature behavior changes.

## Dependencies

Do after:

**Normalize documentation conventions and add an Unreleased changelog workflow**

Prefer after PR #58 merges.

---

# Issue 4 — Normalize Host documentation and member organization

## Goal

Perform a repository-wide documentation/readability normalization of `DovahLink.Host` and its tests using the new documentation conventions.

This is deliberately **not** a documentation-reduction task.

DovahLink should continue documenting every declaration.

The goal is to make that documentation easy for a human to scan without losing focus.

No runtime behavior should change.

## Ordering

Do this after:

1. **Normalize documentation conventions and add an Unreleased changelog workflow**
2. **Normalize Host composition, DI scopes and service lifetimes**

Doing the composition work first avoids documenting/reorganizing code that will immediately move.

## Scope

Audit Host production code and Host tests, including:

```text
host/DovahLink.Host/
host/DovahLink.Host.Tests/
```

and any Host-specific support/tooling touched by the conventions.

## Keep full documentation coverage

Every handwritten declaration should remain documented.

Preserve useful XML such as:

```xml
<summary>
<param>
<typeparam>
<returns>
<exception>
<inheritdoc/>
```

Do not delete useful parameter/result documentation merely to reduce LOC.

## Reduce excessive documentation

Rewrite oversized comments so each declaration contains only the information necessary to understand its current contract.

Typical target:

```csharp
/// <summary>
/// Establishes a fresh baseline and commits it only while its recovery
/// attempt and play context remain current.
/// </summary>
/// <param name="areaId">The state area to establish.</param>
/// <param name="correlationMessageId">The request the baseline answers.</param>
```

Avoid a 20-40 line explanation of:

* each lock;
* every state transition;
* previous fixes;
* PR history;
* future implementation plans.

## Preserve critical contracts

Do not blindly shorten important documentation.

Keep enough information to protect subtle behavior around:

* authentication/admission;
* trust;
* session invalidation;
* pairing;
* outbound priority;
* recovery barriers;
* baseline-before-events;
* revision ordering;
* play-context transitions;
* bounded queues;
* shutdown;
* concurrency;
* ownership/lifetime;
* failure behavior.

Interfaces that define genuinely complex behavior may need more documentation than ordinary methods.

The objective is clarity, not arbitrary line-count reduction.

## Remove historical/future narration

Remove or rewrite phrases such as:

* "previous implementation";
* "earlier fix";
* "before this change";
* "this remained after the first fix";
* "later concept";
* "future phase";
* "Stage X";
* PR/reviewer references.

If that information matters historically, Git/PR/CHANGELOG owns it.

If future work matters, roadmap/issues own it.

If it is a real current compatibility constraint, retain it in current-state wording.

## Method-body comments

Audit comments inside method implementations.

Delete comments that merely narrate the code.

Keep short comments explaining non-obvious **why**, especially:

* synchronization ordering;
* lock avoidance;
* security ordering;
* cancellation/lifetime edge cases;
* non-obvious queue/recovery behavior.

Prefer 1-3 lines.

Do not place long design documentation inside method bodies.

## Test documentation

Tests should remain documented.

However, avoid large comments explaining the genealogy of a regression.

For example, instead of explaining:

```text
this is the race window that remained after the previous barrier fix...
```

describe the current invariant:

```text
Verifies that an event received while acquiring a recovery baseline
is retained until that baseline is established.
```

Test documentation should explain:

* the invariant;
* scenario;
* non-obvious timing/concurrency setup.

It should not preserve the history of every previous failed implementation.

## Member organization

Normalize C# classes to semantic ordering where safe:

1. constants/static state;
2. injected dependencies;
3. instance state;
4. constructors;
5. properties/events;
6. interface/override methods;
7. other public/internal methods;
8. private helpers;
9. nested types.

Keep interface implementation methods grouped together.

Do not put newly added properties at the bottom of a class merely because they were added later.

Do not interleave private helpers between interface methods without a strong reason.

## Inheritance documentation

Use `<inheritdoc/>` when an implementation does not change the contract.

Do not duplicate entire interface documentation on every implementation.

Add implementation-specific documentation only when the implementation adds meaningful behavior/constraints.

## Keep IntelliSense excellent

A developer should be able to hover:

* a service;
* constructor;
* parameter;
* method;
* result;

and immediately understand what it means without reading an essay.

That is the target.

## Tests / validation

This task must not alter functionality.

Run the complete Host test suite.

Any member reorganization or documentation-only changes should produce no behavior difference.

Update documentation/convention consistency tests where needed.

## Acceptance criteria

* Every existing handwritten Host declaration remains documented.
* Useful params/results/exceptions remain.
* Oversized XML blocks are reduced to current contract information.
* Method bodies no longer contain long explanatory documentation.
* Important concurrency/security/lifecycle comments remain.
* Historical/future-development narration is removed from implementation documentation.
* Test docs describe current invariants rather than regression genealogy.
* C# members use consistent semantic ordering.
* Interface methods are grouped before private helpers.
* Host tests remain green.
* No runtime behavior changes.

---

# Issue 5 — Normalize Adapter documentation and member organization

## Goal

Perform the same documentation/readability normalization for the SKSE C++ Adapter and its tests while preserving all safety-critical SKSE/Windows/ownership rationale.

This is not a reduction in documentation coverage.

Every handwritten declaration should continue to be documented.

No runtime behavior should change.

## Ordering

Do this after:

1. **Normalize documentation conventions and add an Unreleased changelog workflow**
2. **Normalize Adapter runtime composition and process-lifetime ownership**

This avoids cleaning/reorganizing code that the composition task will immediately change.

## Scope

Audit the Adapter production code, plugin entry point and Adapter tests.

Include all relevant C++ code in the current Adapter architecture.

## Keep documentation coverage

Keep concise Doxygen-compatible documentation for handwritten:

* classes/interfaces;
* structs;
* enums and enum values;
* type aliases;
* constructors/destructors;
* fields;
* methods;
* free functions;
* private helpers;
* file-local helpers;
* test helpers.

Preserve useful:

```text
@param
@return
@throws
@copydoc
@ref
```

when they add meaningful information.

## Documentation economy

Prefer:

* one-line field/member descriptions;
* one-line parameters;
* concise result descriptions;
* short method summaries;
* longer descriptions only where the current contract actually demands it.

Do not allow ordinary APIs to accumulate 20-40 line implementation essays.

## Preserve important Adapter rationale

This task must be conservative around comments protecting non-obvious runtime constraints.

Keep concise current-state explanations for things such as:

* SKSE initialization ordering;
* game-thread requirements;
* CommonLib/runtime restrictions;
* callback lifetime;
* asynchronous completion lifetime;
* exception boundaries;
* process-lifetime ownership;
* Windows loader lock;
* why `DllMain` must not join/wait/destroy worker-owning services;
* include-order constraints;
* ownership of Skyrim/runtime values;
* bounded work inside game callbacks;
* thread handoff requirements.

These comments prevent future maintainers from making dangerous "cleanup" changes and should remain.

## Remove historical genealogy

Remove/rewrite documentation whose main purpose is explaining:

* what the old Bridge did;
* which old class/type existed before;
* which previous attempt failed;
* which PR introduced the current design;
* which roadmap stage changed it;
* what a future stage may replace it with.

Example:

Instead of:

```text
Mirrors bridge/plugin/dovahlink_bridge_plugin.cpp's SetupLogging...
```

prefer:

```text
Configures asynchronous file logging so SKSE callbacks do not block on file I/O.
```

Current purpose > migration history.

## Convention-file cleanup

The C++ style/convention documents themselves contain some long historical explanations of types that previously existed and why they were replaced.

Retain the architectural rule, but shorten the genealogy.

For example, the nested-type rule can primarily state:

> A nested type is appropriate only when structurally inseparable from its owner, such as requiring private/friend access that a normal file boundary cannot express. Small size or single use is not sufficient.

A current example may be retained when useful.

The entire history of every type that failed this rule belongs in Git/PR history, not permanently in the convention.

## Method bodies

Remove long explanatory blocks inside implementation bodies.

Keep short `//` comments for non-obvious reasons.

Example:

```cpp
// Do not join workers here: DLL detach runs under the Windows loader lock.
```

Good.

A 15-line explanation of the project's migration history at the same location is not.

## Member organization

Normalize C++ declarations where doing so is semantically safe.

Keep API-facing/interface methods grouped logically and helpers grouped separately.

However:

### Do not blindly reorder data members

C++ declaration order affects construction/destruction and may affect:

* aggregate initialization;
* designated initialization;
* layout/ABI assumptions.

Review each reordering.

Do not perform cosmetic member-order changes that could change semantics.

## `@copydoc`

Use `@copydoc` for unchanged inherited/interface contracts.

Do not duplicate the same long documentation between interface and implementation.

Add implementation-specific text only when the implementation changes/adds constraints.

## Tests

Keep tests documented, but describe the current invariant rather than the full regression history.

Retain short explanations where:

* concurrency timing is intentionally controlled;
* thread handoff behavior is non-obvious;
* SKSE/platform constraints affect the test;
* a particular ordering is essential.

## Validation

Run all Adapter/C++ tests and relevant structural checks.

Documentation/member reorganization must not change:

* runtime behavior;
* ownership;
* initialization order;
* destruction semantics;
* protocol behavior;
* thread behavior.

## Acceptance criteria

* Every handwritten Adapter declaration remains documented.
* Params/results/Doxygen remain useful and concise.
* Long method-body narration is removed.
* SKSE/Windows/concurrency/ownership safety rationale remains.
* Legacy Bridge/migration genealogy is removed from current implementation docs where not contractually relevant.
* C++ convention documents become more rule-focused and less historical.
* Interface/public methods and private helpers are organized consistently.
* C++ data-member ordering is not changed blindly.
* Adapter tests remain green.
* No runtime behavior changes.

## Block B -- approved refinement after repository-grounded review (2026-09-13)

Yes — that agent review is **good**, and after checking `main` myself, I agree with its conclusions. It also found a couple of concrete things that make the tasks stronger than when we originally wrote them.

### 1. The `Addition convention` is genuinely wrong today

`common.md` currently says **everything gets appended to the end**, explicitly including changelog releases and class/struct members, and even says this intentionally prioritizes line-number stability over reverse chronology.

Meanwhile the consistency test explicitly interprets the **first numeric changelog version** as the currently published version.

So I agree with the agent: **the prose should change, not the changelog/test ordering**.

There is one useful nuance for `[Unreleased]`: the current regex only matches numeric headings:

```python
^## \[(\d+\.\d+\.\d+)\]
```

So adding:

```markdown
## [Unreleased]

## [0.3.3] - ...
```

does **not** break the existing `entry_versions[0] == VERSION` assertion. It will still see `0.3.3` as the first *versioned* entry.

I would add another consistency check saying approximately:

```text
[Unreleased] must be the first changelog section.
```

That gives us both guarantees:

```text
first overall section = [Unreleased]
first numeric version = VERSION
```

Very clean.

---

### 2. I would make the stale C++ convention fix part of **Issue 1**, not wait entirely for Issue 5

This is the biggest adjustment I'd make after the agent's review.

`cpp-style.md` currently literally opens by saying that several rules use the retired `bridge/` layout as their worked example. It then refers throughout the normative rules to things like `IBridgeCallbackRegistry`, `bridge/shared/enums.hpp`, `TokenStore::Reservation`, `SessionManager::Lease`, `WebSocketSession::Socket`, etc.

That's not just overly verbose.

It's problematic because **the convention being used to instruct new Adapter development is expressed in terms of a deleted architecture**.

I originally put most of that cleanup into Issue 5. I'd refine the boundary to:

| Issue                    | Responsibility                                                                                                                                          |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **#1 conventions**       | Make every **normative rule itself current and correct**. Remove/replace deleted Bridge examples where they are necessary to understand/apply the rule. |
| **#5 Adapter doc sweep** | Clean historical/verbose comments throughout actual Adapter implementation/tests and do the broader C++ documentation normalization.                    |

So after Issue 1, `cpp-style.md` should already be safe to hand to an AI agent implementing new Adapter code.

Issue 5 shouldn't be required to make your **rules trustworthy**.

---

### 3. The agent is right about Issues 2/3 not being emergencies

341-line `Program.cs` and ~331-line plugin composition aren't inherently disastrous.

That's actually why I like these tasks.

We're not doing:

> "Program.cs is huge, refactor immediately!"

We're doing:

> "The architecture is now mature enough that lifetime and composition should become explicit before several more systems are built on top."

That's a much stronger reason.

Especially now that Host has:

```text
Host
 ├─ trusted clients
 ├─ multiple simultaneous sessions
 ├─ connections
 ├─ subscriptions
 ├─ control/data lanes
 ├─ recovery
 └─ state publication
```

The **Host → client → session → connection → operation** lifetime distinction is becoming real architectural information, not theoretical abstraction.

PR #58 also really is merged now, as the agent said — merged on **September 13, 2026**, with 25 commits and 49 changed files.

So our previous blocker is gone.

---

### 4. I would slightly strengthen Issue 1's acceptance criteria

After this review, I'd explicitly add these requirements:

> **Ordered-section rules must be semantic, not universal.** Reverse-chronological collections such as `CHANGELOG.md` keep newest entries first. Semantic class organization takes precedence over line-number stability. Append-only ordering remains only where the collection's semantics genuinely require it, such as appropriate enum/ordered-data cases.

And:

> **Convention documents must describe the current repository.** Normative rules must not depend on deleted directories, retired types, or obsolete architecture as their primary worked examples. Historical examples may remain only where they materially clarify a current rule and are clearly non-normative; prefer current Adapter/Host examples or implementation-neutral wording.

That directly incorporates what the agent discovered.

### Overall

I'd rate the agent's review **very positively**. It didn't merely say "looks good", it inspected the things the issues claim are broken and found actual repository evidence.

And its conclusion about the ordering remains exactly right:

```text
#58 ✅ merged
     │
     ▼
#1 Conventions + Unreleased
     │
     ├──────────────┐
     ▼              ▼
#2 Host          #3 Adapter
composition      composition
     │              │
     ▼              ▼
#4 Host docs     #5 Adapter docs
```

I would now document this plan, using phase-build to create the phase package described above.

## Block C -- execution contract, backfill, and package invariants (approved 2026-09-13)

Yes — the overall approach is right, and I actually prefer this over jumping directly into the five implementation PRs.

You are turning the five issues into an execution package with explicit dependencies, frozen requirements and divergence tracking. For DovahLink, where AI agents do a lot of the implementation, that is valuable: the agent doesn't need to reconstruct our discussion differently in PR #4 than it did in PR #1.

It also fits your repository workflow: substantial work is supposed to be incremental and review-gated, and design decisions should be documented before they are encoded in code.

I would keep the 5-concept split exactly as proposed:

```text
01 conventions + changelog
        │
        ├──────────────────┐
        ▼                  ▼
02 Host composition    03 Adapter composition
        │                  │
        ▼                  ▼
04 Host docs           05 Adapter docs
```

That decomposition is clean. I would not merge 2+4 or 3+5 because then architectural movement and mass documentation cleanup become mixed in the same review.

There are, however, a few things I would change before telling it to write the package.

1. `SOURCE.md` is a good idea — but it should really be the frozen source

Since the five GitHub issues don't currently exist as authoritative external objects, a committed `SOURCE.md` is actually better than pretending some chat transcript is the source.

I would make it contain:

* the five original approved task definitions;
* the refinements we approved afterward;
* the repository baseline commit;
* source date;
* ideally a fingerprint/hash;
* explicit statement that it becomes immutable once this plan is approved.

For example:

```text
Source baseline:
main @ 499bd4f44e93e870388ff54f4f91489e3c546064
PR #58 merged
2026-09-13
```

PR #58 really is merged now.

Then:
`SOURCE.md` = what was requested
`PLAN.md` = how we execute it
concepts = design/scope for individual PRs
`DIVERGENCES.md` = intentional deviations from SOURCE

That's excellent provenance.

But don't duplicate the entire SOURCE everywhere

This is the one thing in the agent's wording I don't love:

"full acceptance text preserved verbatim in the concept files"

That risks recreating exactly the documentation problem we're trying to solve. 😄

If the tooling permits it, I'd prefer:

```text
SOURCE.md
  R1.1
  R1.2
  ...
  R5.10

01-conventions-and-changelog.md
  Implements: R1.1–R1.14
  Design
  Scope
  Acceptance
  Tests
  Non-goals
```

The concept can restate acceptance requirements concisely, but SOURCE owns the exact original wording.

That way there is only one verbatim copy.

2. Add an explicit PR execution contract to `PLAN.md`

This is important.

The package shouldn't merely show dependencies. It should say how agents are allowed to execute it.

I'd add:

```text
Each concept is implemented in its own feature branch and PR.

A dependent concept must not begin until its dependency is merged to main.

After Concept 01:
- Concepts 02 and 03 may proceed independently/in parallel.

Concept 04 requires Concept 02 merged.
Concept 05 requires Concept 03 merged.

Stop for maintainer review after every concept/PR.
Do not automatically continue to the next concept after completing one.
```

That maps perfectly to your existing AI-development rule requiring multi-file refactors to be incremental and review-gated.

So the real sequence becomes:

```text
Planning PR
     │
     ▼
PR 01 conventions
     │
 ┌───┴───┐
 ▼       ▼
PR 02   PR 03
 │        │
 ▼        ▼
PR 04   PR 05
```

Technically that's six PRs if the planning package itself is committed separately.

I think that's the cleanest way.

3. Very important: `[Unreleased]` must be backfilled

This is the biggest thing missing from the proposed package.

Your current published version is still:

```text
0.3.3
```

But a lot has happened on `main` since 0.3.3, including the entire Host/Adapter migration work and now PR #58.

So Concept 01 must not simply add:

```markdown
## [Unreleased]
```

and leave it empty.

Otherwise the new system begins by already losing the exact history you're trying to preserve.

Concept 01 should explicitly require:

When introducing `[Unreleased]`, audit notable changes merged to `main` since the `0.3.3` release baseline and backfill the section with concise current outcomes.

Not commit-by-commit.

Not every refactor.

Just notable things that would have been put into `[Unreleased]` had the rule existed at the time.

This is especially important because your whole reason for `[Unreleased]` was:

never again lose track of what has already been implemented but hasn't been released.

I would make that R1.15.

4. The agent's D1 split is correct

This part is exactly right:

```text
Concept 01:
fix cpp-style's normative correctness

Concept 05:
clean Adapter implementation/test documentation
```

That's a meaningful distinction.

Right now `cpp-style.md` explicitly says it uses the retired `bridge/` layout as a worked example, and then normative rules reference deleted Bridge types and paths.

A convention file instructing future agents cannot wait until PR 05 to become trustworthy.

So:

01 owns

* deleted-path references in actual rules;
* obsolete normative examples;
* Bridge-specific rules that need translation to Adapter reality.

05 owns

* verbosity cleanup;
* historical comments in Adapter code;
* Doxygen cleanup;
* test docs;
* member organization.

Logging that as D1 is exactly what `DIVERGENCES.md` should be for.

5. Add one more global invariant to PLAN

All five concepts should inherit:

No opportunistic cleanup.

This is already part of DovahLink's AI rules, but it's worth repeating in this package because "normalization" can become dangerous very quickly.

For example Concept 04 should not become:

"While cleaning documentation I noticed this state machine is ugly, so I rewrote it."

Same for DI.

Put something like:

```text
Unless a concept explicitly states otherwise:

- preserve runtime behavior;
- preserve protocol behavior;
- preserve security semantics;
- preserve public behavior;
- do not introduce unrelated architectural cleanup;
- any newly discovered architectural problem is reported separately rather than silently fixed.
```

Perfect guardrail for an AI agent.

6. Track completion without rewriting history

I'd have `PLAN.md` carry execution status:

```markdown
| Concept | Status | PR | Merge SHA |
|---|---|---|---|
| 01 | Planned | — | — |
| 02 | Blocked by 01 | — | — |
| 03 | Blocked by 01 | — | — |
| 04 | Blocked by 02 | — | — |
| 05 | Blocked by 03 | — | — |
```

After each PR:

```text
Planned → In progress → Complete
```

And record the merged PR/SHA.

But don't rewrite `SOURCE.md` after implementation.

That gives you a really nice audit chain:

```text
SOURCE
  ↓
PLAN
  ↓
CONCEPT
  ↓
PR
  ↓
MERGE SHA
```

For a project you want to keep for years, that's excellent.

So which option would I choose in your screenshot?

Not quite "Write it as proposed" yet.

I would choose Other rather than "Adjust the concept split," because I do not want to change the five concepts.

Give the agent this:

Keep the proposed 5-concept split and dependency graph. Before writing, make these adjustments:

1. `SOURCE.md` is the immutable canonical copy of the approved requirements; concept files should trace to requirement IDs and avoid unnecessary verbatim duplication of the full source.
2. Record the current `main` commit/PR #58 merge as the planning baseline.
3. Add an explicit execution rule to `PLAN.md`: one PR per concept, stop for maintainer review after every PR, 02/03 may start only after 01 is merged, 04 after 02, and 05 after 03.
4. Add a Concept 01 requirement to backfill `[Unreleased]` with notable changes already merged since release 0.3.3 rather than starting it empty.
5. Keep D1 exactly as proposed: normative stale Bridge references move to Concept 01; broader Adapter implementation/test documentation cleanup remains Concept 05.
6. Add a package-wide invariant: no runtime/protocol/security behavior changes or opportunistic cleanup unless explicitly required by the concept; newly discovered unrelated issues are reported separately.
7. Let `PLAN.md` track concept status, PR number and merge SHA; do not mutate `SOURCE.md` as work progresses.

Then write the package.

After those changes, yes, I would approve the package and start Concept 01.

This is considerably better than five disconnected issues. You're essentially giving future agents a small program of work with traceability and review gates, which fits DovahLink very well.
