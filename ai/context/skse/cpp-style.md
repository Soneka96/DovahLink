# SKSE C++ style

These conventions govern native SKSE/C++ work in this repository, currently `adapter/`, whose
module layout is `capture/`, `dispatch/`, `identity/`, `ipc/`, `papyrus/`, `plugin/`, `process/`,
and `runtime/`. `adapter/` is one CMake target and the only component permitted to depend directly
on CommonLib or Skyrim runtime types, per `ai/context/adapter/architecture.md`'s "Technology
boundary". A source file's `commonlib_` filename prefix marks it as one of those CommonLib-touching
files, distinguishing it from its CommonLib-free counterpart within the same module where one
exists.

## Ownership and lifetime

- Prefer RAII and standard-library ownership types.
- Make ownership visible; avoid owning raw pointers.
- Do not retain borrowed Skyrim objects beyond the lifetime guaranteed by the runtime API.
- Keep long-lived workers and connections owned by one clear application component.
- Make shutdown idempotent.
- Never reorder existing data members without first confirming the change preserves construction
  order, destruction order, aggregate/designated initialization, and layout/ABI assumptions --
  declaration order for data members is part of a type's actual behavior, not merely its
  readability. When in doubt, leave existing data-member declaration order exactly as-is.
- Transport completion callbacks must use an in-flight counter or a lifetime token owned independently of the coordinator so no callback can access destroyed coordinator or transport state. The token remains valid until every callback has returned.
- Catch all exceptions at callback, worker-thread, and transport-completion boundaries. Convert them into controlled component failure and diagnostics; never allow an exception to escape a callback or thread entry point.

## Behavior-bearing boundaries

- Every behavior-bearing C++ class or equivalent type has an explicit narrow interface or
  pure-virtual contract, even when it currently has one implementation. Consumers depend on that
  interface rather than the concrete type.
- A C++ behavior-bearing implementation implements exactly one DovahLink-owned interface, named
  `I<ClassName>`, and that interface is declared in the same owning header as the concrete class,
  except for the narrowly defined CommonLib dependency-wall case documented below for
  `IAdapterPairingNotificationSink` and `CommonLibAdapterPairingNotificationSink`. DovahLink-owned
  interfaces never inherit from one another. A required CommonLib/Skyrim framework base is the only
  inheritance exception.
- Every collaborator is supplied through the constructor. Do not construct or resolve a
  behavior-bearing collaborator inside another class.
- DTOs, protocol/value types, enums, pure functions, and other data-only types are not wrapped in
  artificial interfaces. This rule is adopted phase-forward and does not reopen completed phases.
- A free function that accepts two or more plugin/connection-lifetime collaborators (values
  identical across every call for the process's lifetime, typically an injected `I<ClassName>&`)
  alongside per-call data (values that vary each invocation, such as the specific message or
  timestamp being processed) must instead be expressed as a class: the lifetime collaborators
  become constructor-injected fields, following the same `I<ClassName>` interface rule above, and
  the function becomes a method taking only the data that varies per call. Exempt: a private,
  file-local helper called from exactly one place inside a single larger function or class, since
  it is an extracted fragment of its caller's own body rather than an independent production entry
  point; and a framework-mandated plain-function signature (for example an SKSE Papyrus-bound
  native function, which SKSE requires as a captureless function pointer), the same category of
  hard external constraint as `IAdapterPairingNotificationSink`'s dependency-wall exception below.
  This rule is adopted phase-forward and does not reopen completed phases.

## Files and types

- Keep one primary class or component per file. A DovahLink interface and its one concrete
  implementation are the single paired declaration exception; unrelated structs, result types, and
  values remain in their own files.
- Per `ai/context/common.md`'s file-organization rule, a small result/outcome value type is not
  automatically "inseparable" merely because it is currently returned by only one method: it still
  gets its own file, unnested, at namespace scope. "Inseparable" means genuine structural coupling a
  file boundary cannot express, such as a `friend`-only RAII helper that manipulates its owner's
  private state through members no public interface exposes -- a `friend`-only nested type cannot
  satisfy `common.md`'s "Behavioral boundaries and test isolation" rule, since a mock implementing
  the owning interface would have no way to construct one. `adapter/` currently has no instance of
  this carve-out; do not manufacture one merely because a type is small or currently used in one
  place -- a plain data-only result type still gets its own file even when only one caller currently
  constructs it.
- Absolute rule: every `commonlib_`-prefixed header or source file in `adapter/` that directly
  includes an `RE/...` or `SKSE/...` runtime header must include those runtime headers before any
  DovahLink-owned application or protocol header and before any third-party header. The pinned
  CommonLibSSE-NG `SKSE/Impl/WinAPI.h` redeclares Windows names and is not safe after Boost or
  Windows SDK headers have imported their macros. Keep the CommonLib-free interface in its own,
  non-`commonlib_`-prefixed header so this order does not leak CommonLib into neutral application
  code. Structural include-order tests must cover every `commonlib_`-prefixed file that can import
  those dependencies.
- Every enum in `adapter/` is a single project-wide exception to the file-organization rule, per
  `ai/context/common.md`'s "not a repository-wide dumping ground" -- `adapter/` is one compilation
  unit/project (one CMake target), not several, so its module subdirectories (`capture/`,
  `dispatch/`, `identity/`, `ipc/`, `papyrus/`, `plugin/`, `process/`, `runtime/`) are not separate
  packages the way, for example, the Flutter app and the SDK are for
  `ai/context/dart/dart-style.md`'s per-package `enums.dart` rule, or the way each C# project gets
  its own `Enums.cs` per `ai/context/dotnet/csharp-style.md`; this mirrors that same rule at the
  correct granularity for this language. Every `adapter/` enum belongs in one project-wide
  `adapter/enums.hpp`, with each domain's enums kept in that domain's own nested namespace (for
  example `dovahlink::adapter::ipc`, `dovahlink::adapter::capture`) and grouped into sections by
  conceptual owner, each preceded by a `// ---- <Area> ----` comment banner -- one physical file
  does not require flattening domain namespaces into it. `adapter/`'s enums currently live in
  `ipc/ipc_enums.hpp` pending a physical normalization to `adapter/enums.hpp`; treat that as today's
  location, not the intended one, and do not add a second, competing enum file to any other module
  in the meantime. A nested enum that exists purely as a scoped selector for its own owning type's
  public API is not required to move: it is not a top-level `adapter/` enum declaration, the same
  way a nested carve-out type is not subject to the one-type-per-file default above. A test-file-local
  enum used only for that test file's own internal parametrization is out of scope for the same
  reason: it is test scaffolding, not a production `adapter/` declaration. Centralizing every enum's
  *declaration* in one file does not loosen module dependency discipline: a module may still only
  use enum concepts from domains it is already allowed to depend on, per whatever ownership
  boundaries currently apply to that module. This is enforced by reviewing usage sites, not by file
  structure -- C++ has no per-symbol include restriction.
- Every small cross-cutting constant value (timeouts, limits, and similar) belongs in one
  project-wide `adapter/constants.hpp`, mirroring the enum rule above: each domain's constants kept
  in that domain's own nested namespace, grouped into sections by the area they belong to, each
  preceded by a `// ---- <Area> ----` comment banner. `adapter/`'s constants currently live in each
  module's own `constants.hpp`, per module directory as listed above, pending the same physical
  normalization as the enum file; treat that per-module layout as today's location, not the intended
  one.
- Keep game-runtime types out of neutral application and protocol headers.
- Use explicit names for runtime adapters, application values, wire messages, and transport errors.
- Keep protocol serialization in dedicated mapping code rather than spreading it through game adapters.
- A DovahLink port and its one concrete implementation may split into two independent files --
  interface alone in one, implementation alone in the other -- only when a real CommonLib
  dependency wall makes the normal paired-file rule impossible to satisfy, never as a default
  alternative to it. The condition: the implementation directly touches CommonLib/Skyrim runtime
  types (so it can only be compiled with `RE/Skyrim.h` in scope), while the port's consumer needs to
  stay includable without pulling CommonLib into neutral application code.
  `IAdapterPairingNotificationSink` (`ipc/adapter_pairing_notification_sink.hpp`, CommonLib-free)
  and `CommonLibAdapterPairingNotificationSink`
  (`ipc/commonlib_adapter_pairing_notification_sink.hpp`/`.cpp`, whose implementation calls
  `RE::DebugNotification`) are one current instance of this split. `IAdapterTaskMarshaller`
  (`runtime/adapter_task_marshaller.hpp`, CommonLib-free) and `CommonLibAdapterTaskMarshaller`
  (`runtime/commonlib_adapter_task_marshaller.hpp`/`.cpp`, compiled against SKSE's own task-interface
  mechanism) are the same shape for the same underlying reason and are this codebase's second
  instance -- confirming the split is a real, recurring necessity rather than a one-off. Do not
  reach for this split to avoid writing a file-placement justification, to keep a file shorter, or
  for any port whose implementation could simply live beside it in one file; a false positive here
  quietly refragments the paired-file rule this exception exists to preserve everywhere else.

## Member ordering

Order a class's methods semantically, by role, mirroring `ai/context/dotnet/csharp-style.md`'s
member-ordering rule: constants/static state, injected dependencies, mutable instance state,
constructors and destructor, interface-implementation and override methods (kept together as one
group), other public/internal methods, private helper methods, nested types. A newly added method
goes where its role places it in this order, not automatically appended after the last existing
member of its kind. Data members are the one deliberate exception -- see "Ownership and lifetime"
above before reordering them.

## Parameter grouping and context objects

- Use a request value type for per-call operation data only when its fields form one operation-level
  contract and grouping makes their coupling, invariant, provenance, or lifetime relationship explicit.
  Do not group fields merely to reduce parameter count or because they are available together. A reviewer
  should be able to state the grouping rationale in one sentence.
- Classify project-owned composites by semantics, not by names, aliases, wrappers, nesting, or parameter
  count. Apply the same review recursively to project-owned nested composites; do not reinterpret
  standard-library or third-party types. For every field, identify the specific role it has in the same
  operation contract or invariant; a vague association, shared provenance, or future convenience is not
  sufficient. Do not add fields for future extensibility, convenience access, or test setup.
- `adapter/` targets C++23. Public aggregate requests may use the C++20 designated-initialization
  feature for readability. Designators name only direct non-static data members and, when used, must
  follow declaration order; later members may be omitted. Omitted members use their default member
  initializer, if present, otherwise empty list-initialization, which may value-initialize, invoke a
  constructor, or be ill-formed. Designated initialization does not make fields required, cannot be
  enforced as the only construction syntax, and does not remove the risks of positional initialization.
  Treat aggregate members and declaration order as API: adding, removing, reordering, or changing a
  member can break callers or change the meaning of existing initialization.
- If omission must be rejected, ensure initialization of the omitted member from `{}` is ill-formed;
  omitting a default member initializer alone is insufficient. If supplied values or cross-field
  relationships must be validated, use member types whose public construction paths enforce their
  invariant or use a non-aggregate with inaccessible representation and enforcing constructors,
  factories, or mutators. A factory is not an invariant boundary if direct construction or mutation
  remains available.
- Treat a request as value-like rather than inherently immutable when it exposes public aggregate
  members. `const` makes only that interface read-only; it does not provide deep immutability or prevent
  mutation through aliases. Use an encapsulated type when the invariant must hold for the object's
  lifetime.
- Use a focused class for state or dependencies required across calls by one cohesive capability, state
  machine, or lifecycle. Each stored dependency must directly support that responsibility, be used by
  production behavior, and have an explicit ownership and validity contract. Constructor injection
  requires dependencies to be supplied; it does not establish ownership, lifetime, or validity.
  References, reference wrappers, string views, and spans are non-owning; document their external owner,
  lifetime, invalidation, and mutability requirements. Use an owning value or smart pointer only
  according to the actual ownership model.
- Avoid catch-all composite types whose members are unrelated or grow opportunistically, regardless of
  whether they are named `Context`, `Options`, `Dependencies`, `Request`, `State`, `View`, or something
  else. Domain-specific composites are acceptable only when every field belongs to the same operation
  contract or invariant. Do not pass a composite orchestration object to a leaf handler; pass only the
  data or narrow capability it directly requires. A capability must not expose its owner, connection,
  broad session object, context, registry, broad getter, downcast, or unrelated operation. A narrow
  session identifier or other scalar value is acceptable when it is directly part of the leaf contract.
- Treat non-owning references, reference wrappers, string views, and spans as especially risky in queued,
  deferred, or asynchronous work: copying the request does not extend the source lifetime. Document the
  external owner, lifetime, invalidation, and mutability requirements, and use an owning representation
  when the work may outlive the source.
- Test each documented semantic branch, representative category of representable invalid input, and
  observable construction or handling failure. Record or run compile-time and construction-constraint
  tests for states impossible through the supported public API instead of requiring runtime tests for
  impossible values. Do not add production API surface solely for tests.

## Documentation

Follow the shared documentation rules in `ai/context/common.md`.

- Use concise Doxygen-compatible `///` documentation directly above every handwritten class,
  struct, enum and enum member, type alias, constructor, destructor, data member, method, and free
  function, regardless of visibility. This includes private helpers, file-local helpers, and test
  helpers.
- Document public and protected APIs on their declarations in header files. Do not duplicate the
  same documentation on an out-of-line definition in a `.cpp` file.
- Document a private or file-local function directly above its definition when it has no separate
  declaration.
- Document each parameter with `@param`, normally in one line; document a non-`void` return value
  with `@return`, normally in one or two lines; document an exception that is part of the
  function's contract with `@throws`. Do not omit one of these merely because the parameter name or
  return type already reads clearly on its own -- their job is to keep the complete contract
  visible at the declaration. Use `@ref` for links to C++ symbols.
- When an override keeps the inherited contract unchanged, use
  `/// @copydoc BaseType::Method` with the actual source symbol rather than copying documentation.
  Add separate text only for changed preconditions, side effects, or guarantees.
- Keep namespace-closing comments separate from documentation for the following declaration.

## Error handling

- Handle expected failures at the boundary where they occur.
- Never let an infrastructure exception or error code silently become valid game state.
- Include enough context in logs to identify the stage, message, and runtime without logging sensitive pairing data.
- Keep logging out of protocol payloads and game state.

## Performance and safety

- Do not allocate unnecessarily or perform unbounded work in a game callback.
- Any callback allocation or runtime traversal must have an explicit bound and maintainer-approved reason; prefer copying a small, validated value into preallocated or bounded storage.
- Bound queues and define behavior when the client is slower than the game state producer.
- Treat missing, delayed, and stale values explicitly; do not substitute plausible values silently.
- Keep the first implementation read-only and minimize hooks.

## Dependencies

- Use the approved SKSE/CommonLib toolchain and existing project utilities before adding a dependency.
- Do not add a dependency solely to avoid a small, well-understood adapter.
- Document a dependency's role, version constraints, and runtime impact when it is introduced.
