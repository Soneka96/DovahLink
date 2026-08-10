# SKSE C++ style

## Ownership and lifetime

- Prefer RAII and standard-library ownership types.
- Make ownership visible; avoid owning raw pointers.
- Do not retain borrowed Skyrim objects beyond the lifetime guaranteed by the runtime API.
- Keep long-lived workers and connections owned by one clear application component.
- Make shutdown idempotent.

## Files and types

- Keep one primary class or component per file unless the types are inseparable declarations and definitions.
- Keep game-runtime types out of neutral application and protocol headers.
- Use explicit names for runtime adapters, application values, wire messages, and transport errors.
- Keep protocol serialization in dedicated mapping code rather than spreading it through game adapters.

## Error handling

- Handle expected failures at the boundary where they occur.
- Never let an infrastructure exception or error code silently become valid game state.
- Include enough context in logs to identify the stage, message, and runtime without logging sensitive pairing data.
- Keep logging out of protocol payloads and game state.

## Performance and safety

- Do not allocate unnecessarily or perform unbounded work in a game callback.
- Bound queues and define behavior when the client is slower than the game state producer.
- Treat missing, delayed, and stale values explicitly; do not substitute plausible values silently.
- Keep the first implementation read-only and minimize hooks.

## Dependencies

- Use the approved SKSE/CommonLib toolchain and existing project utilities before adding a dependency.
- Do not add a dependency solely to avoid a small, well-understood adapter.
- Document a dependency's role, version constraints, and runtime impact when it is introduced.
