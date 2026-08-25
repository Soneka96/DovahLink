# SKSE C++ style

## Ownership and lifetime

- Prefer RAII and standard-library ownership types.
- Make ownership visible; avoid owning raw pointers.
- Do not retain borrowed Skyrim objects beyond the lifetime guaranteed by the runtime API.
- Keep long-lived workers and connections owned by one clear application component.
- Make shutdown idempotent.
- Transport completion callbacks must use an in-flight counter or a lifetime token owned independently of the coordinator so no callback can access destroyed coordinator or transport state. The token remains valid until every callback has returned.
- Catch all exceptions at callback, worker-thread, and transport-completion boundaries. Convert them into controlled component failure and diagnostics; never allow an exception to escape a callback or thread entry point.

## Behavior-bearing boundaries

- Every behavior-bearing C++ class or equivalent type has an explicit narrow interface or
  pure-virtual contract, even when it currently has one implementation. Consumers depend on that
  interface rather than the concrete type.
- Every collaborator is supplied through the constructor. Do not construct or resolve a
  behavior-bearing collaborator inside another class.
- DTOs, protocol/value types, enums, pure functions, and other data-only types are not wrapped in
  artificial interfaces. This rule is adopted phase-forward and does not reopen completed phases.

## Files and types

- Keep one primary class or component per file unless the types are inseparable declarations and definitions.
- Per `ai/context/common.md`'s file-organization rule, a small result/outcome value type is not
  automatically "inseparable" merely because it is currently returned by only one method: it still
  gets its own file, unnested, at namespace scope -- the same treatment an enum gets before it is
  consolidated into `bridge/shared/enums.hpp`. "Inseparable" means genuine structural coupling a
  file boundary cannot express, such as a `friend`-only RAII helper that manipulates its owner's
  private state: `TokenStore::Reservation` and `SessionManager::Lease` are the two carve-outs in
  this codebase today, and they stay nested for that reason. A plain data-only result struct such
  as `PairingSession`'s `StartChallengeResult` has no such coupling and does not qualify.
- Every enum in `bridge/` is a single Bridge-wide exception to the file-organization rule, per
  `ai/context/common.md`'s "not a repository-wide dumping ground" -- `bridge/` is one compilation
  unit/project (one CMake target), not several, so the module subdirectories
  (`bridge/application/`, `bridge/game_state/`, `bridge/protocol/`, `bridge/security/`,
  `bridge/transport/`, `bridge/plugin/`) are not separate packages the way, for example, the
  Flutter app and the SDK are for `ai/context/dart/dart-style.md`'s per-package `enums.dart` rule;
  this mirrors that same rule at the correct granularity for this language. Every enum belongs in
  `bridge/shared/enums.hpp`, grouped into sections by conceptual owner (`Application`, `Security`,
  `Transport`, `Protocol`, and so on as new areas are added), each preceded by a
  `// ---- <Area> ----` comment banner; a section may nest finer sub-banners of its own where that
  adds real information (for example Security's `Developer token`/`Pairing`/`Known device`/`Factory
  reset` groupings). A nested enum that exists purely as a scoped selector for its own owning
  type's public API (for example `LoopbackListener::IpVersion`) is not required to move: it is not
  a top-level Bridge enum declaration, the same way a nested carve-out type is not subject to the
  one-type-per-file default above. A test-file-local enum used only for that test file's own
  internal parametrization (for example `coordinator_failure_test.cpp`'s `ShutdownFailureStage`)
  is out of scope for the same reason: it is test scaffolding, not a production Bridge
  declaration. Centralizing every enum's *declaration* in one file does not loosen
  `ai/context/skse/architecture.md`'s dependency-edge rules: a module may still only use enum
  concepts from domains it is already allowed to depend on (for example `bridge/transport/` must
  not start depending on a `bridge/security/`-owned enum merely because it is easier to reach from
  one shared header). This is enforced by reviewing usage sites, not by file structure -- C++ has
  no per-symbol include restriction. `bridge/shared/` holds only `enums.hpp` for now; it is not a
  general-purpose utilities location, and adding anything else there needs its own maintainer
  decision.
- Every small cross-cutting constant value (timeouts, limits, and similar) belongs in that module's
  own `constants.hpp`, per module directory as listed above -- never shared across module
  directories, and not consolidated bridge-wide like enums are. Group entries within it by the
  area they belong to, each preceded by a `// ---- <Area> ----` comment banner.
- Keep game-runtime types out of neutral application and protocol headers.
- Use explicit names for runtime adapters, application values, wire messages, and transport errors.
- Keep protocol serialization in dedicated mapping code rather than spreading it through game adapters.

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
- The bridge targets C++23. Public aggregate requests may use the C++20 designated-initialization
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
- Use `@param`, `@return`, and `@throws` only when they add contract information beyond the signature
  and summary. Use `@ref` for links to C++ symbols.
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
