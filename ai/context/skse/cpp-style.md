# SKSE C++ style

## Ownership and lifetime

- Prefer RAII and standard-library ownership types.
- Make ownership visible; avoid owning raw pointers.
- Do not retain borrowed Skyrim objects beyond the lifetime guaranteed by the runtime API.
- Keep long-lived workers and connections owned by one clear application component.
- Make shutdown idempotent.
- Transport completion callbacks must use an in-flight counter or a lifetime token owned independently of the coordinator so no callback can access destroyed coordinator or transport state. The token remains valid until every callback has returned.
- Catch all exceptions at callback, worker-thread, and transport-completion boundaries. Convert them into controlled component failure and diagnostics; never allow an exception to escape a callback or thread entry point.

## Files and types

- Keep one primary class or component per file unless the types are inseparable declarations and definitions.
- Keep game-runtime types out of neutral application and protocol headers.
- Use explicit names for runtime adapters, application values, wire messages, and transport errors.
- Keep protocol serialization in dedicated mapping code rather than spreading it through game adapters.

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
