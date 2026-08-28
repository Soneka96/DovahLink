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
- A C++ behavior-bearing implementation implements exactly one DovahLink-owned interface, named
  `I<ClassName>`, and that interface is declared in the same owning header as the concrete class,
  except for the narrowly defined CommonLib target dependency-wall case documented below for
  `IBridgeCallbackRegistry` and `BridgeCallbackRegistry`. DovahLink-owned interfaces never inherit
  from one another. A required CommonLib/Skyrim framework base is the only inheritance exception.
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
  hard external constraint as `IBridgeCallbackRegistry`'s dependency-wall exception below. This
  rule is adopted phase-forward and does not reopen completed phases.

## Files and types

- Keep one primary class or component per file. A DovahLink interface and its one concrete
  implementation are the single paired declaration exception; unrelated structs, result types, and
  values remain in their own files.
- Per `ai/context/common.md`'s file-organization rule, a small result/outcome value type is not
  automatically "inseparable" merely because it is currently returned by only one method: it still
  gets its own file, unnested, at namespace scope -- the same treatment an enum gets before it is
  consolidated into `bridge/shared/enums.hpp`. "Inseparable" means genuine structural coupling a
  file boundary cannot express, such as a `friend`-only RAII helper that manipulates its owner's
  private state. `TokenStore::Reservation`, `SessionManager::Lease`, and `ConnectionSlot::Lease`
  were three prior examples that turned out not to qualify, and each has since been replaced by a
  publicly-constructible type instead (the shared `ScopedRelease` RAII utility for the latter two;
  a standalone, non-nested `TokenReservation` for the former, since its
  hold-a-lock-then-explicit-`Commit` shape differs from `ScopedRelease`'s auto-release-on-drop
  shape) precisely so an interface's test double can construct one without `friend` access -- a
  `friend`-only nested type cannot satisfy `common.md`'s "Behavioral boundaries and test isolation"
  rule, since a mock implementing the owning interface has no way to construct one. A plain
  data-only result struct such as `PairingSession`'s `StartChallengeResult` has no such coupling
  and does not qualify. `WebSocketSession::Socket` (`transport/websocket_session.hpp`) is this
  codebase's one current instance of a type that does qualify: every method beyond the two exposed
  through its own `ISocket` interface is private and reached only through `friend class
  WebSocketSession`, which manipulates `Socket`'s `stream_`/`ioContext_` directly across nearly
  every `WebSocketSession` method (`Accept`, `ReadMessage`, `WriteMessage`, `Close`,
  `SetTimeoutPolicy`, and others) as an extension of its own state, not merely as a client calling
  a self-contained API -- the same bar the three replaced examples failed to meet. `ISocket` itself
  still exists and is not exempt: `Socket`'s two behavior-bearing operations that an external
  collaborator (`application::IActiveSessionSocket`/`IActiveSessionController`) actually consumes
  are on that narrow interface, constructed only via `WebSocketSession::CreateSocket`; the carve-out
  covers only the file-placement question for `Socket`'s remaining, `WebSocketSession`-only surface.
- Absolute rule: every header or source file in `bridge/game_state/` that directly includes an
  `RE/...` or `SKSE/...` runtime header must include those runtime headers before any
  DovahLink-owned application, game-state, transport, or protocol header and before any third-party
  header. The pinned CommonLibSSE-NG `SKSE/Impl/WinAPI.h` redeclares Windows names and is not safe
  after Boost or Windows SDK headers have imported their macros. Keep runtime-free interfaces in
  separate headers so this order does not leak CommonLib into neutral application code. Structural
  include-order tests must cover every runtime adapter whose header or source can import those
  dependencies.
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
  no per-symbol include restriction. `bridge/shared/` holds `enums.hpp` and `scoped_release.hpp`/
  `.cpp` (the `ScopedRelease` RAII utility, genuinely used across `application/`, `security/`, and
  `transport/` and owned by none of them); it is not a general-purpose utilities location, and
  adding anything else there needs its own maintainer decision.
- Every small cross-cutting constant value (timeouts, limits, and similar) belongs in that module's
  own `constants.hpp`, per module directory as listed above -- never shared across module
  directories, and not consolidated bridge-wide like enums are. Group entries within it by the
  area they belong to, each preceded by a `// ---- <Area> ----` comment banner.
- Keep game-runtime types out of neutral application and protocol headers.
- Use explicit names for runtime adapters, application values, wire messages, and transport errors.
- Keep protocol serialization in dedicated mapping code rather than spreading it through game adapters.
- A DovahLink port and its one concrete implementation may split into two independent files --
  interface alone in one, implementation alone in the other -- only when a real CMake-target
  dependency wall makes the normal paired-file rule impossible to satisfy, never as a default
  alternative to it. The condition: the implementation depends on a CommonLib-touching type paired
  with its own CommonLib-dependent sibling in one file (so it can only be compiled into a target
  linked against `CommonLibSSE::CommonLibSSE`), while the port's consumer lives in the
  Skyrim-independent core (so the port itself must stay includable without `RE/Skyrim.h`).
  `IBridgeCallbackRegistry` (`application/i_bridge_callback_registry.hpp`, CommonLib-free) and
  `BridgeCallbackRegistry` (`application/bridge_callback_registry.hpp`/`.cpp`, compiled into
  `dovahlink_bridge_game_state`) confirmed this split necessary empirically, not merely convenient:
  compiling `BridgeCallbackRegistry` into the Skyrim-independent core produced over 100 cascading
  errors from `RE/Skyrim.h` requiring `CommonLibSSE::CommonLibSSE`'s own compile setup.
  `IPairingNotificationSink` (`application/pairing_notification_sink.hpp`, CommonLib-free) and
  `CommonLibPairingNotificationSink` (`game_state/commonlib_pairing_notification_sink.hpp`/`.cpp`,
  compiled into `dovahlink_bridge_game_state` because its implementation calls
  `RE::DebugNotification`) are the same shape for the same underlying reason and are this
  codebase's second instance. Do not reach for this split to avoid writing a file-placement
  justification, to keep a file shorter, or for any port whose implementation could simply live
  beside it in one file; a false positive here quietly refragments the paired-file rule this
  exception exists to preserve everywhere else.

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
