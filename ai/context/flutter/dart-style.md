# Flutter and Dart style

## Never do

- Do not use `print`, `debugPrint`, `developer.log`, direct console writes, or ad-hoc logging packages. Until an approved logger exists, diagnostics must use typed results/state. Never log tokens, credentials, raw protocol payloads, or unredacted sensitive game state.
- Do not use the null assertion operator (`!`) when explicit null handling is possible.
- Do not use `dynamic` or `any`-style escape hatches to avoid modelling a type; the boundary exception for protocol decoding is defined below.
- Do not write snapshot tests.
- Do not add a dependency when the existing SDK or project code is sufficient.

## Naming and files

- Use the naming convention established for the relevant type before adding a file. When no existing example exists, use Dart defaults: `snake_case.dart` filenames, `UpperCamelCase` types, `lowerCamelCase` members, and descriptive protocol suffixes such as `CharacterStateModel` and `CharacterStateMessage`.
- Keep one primary class per file.
- Name files after the concept they contain, not after the screen that happens to use them.
- Keep protocol mapping names explicit so a Flutter model is not confused with a wire message.

## Boundaries

- Keep parsing and serialization at the protocol/client boundary.
- Keep business decisions out of widgets and event callbacks.
- Keep reusable widgets dumb: pass data and callbacks as inputs.
- Do not fetch services or dependencies from a low-level reusable widget.
- Session identity, correlation, stale-message handling, and revision transitions belong in the protocol/client-state adapter.
- Use explicit names such as `connecting`, `connected`, `recovering`, `disconnected`, `stale`, and `failed`; do not collapse distinct protocol states into a generic `loading` flag.
- User-visible error, disconnected, stale, and recovery states must expose typed, user-safe status models or localized messages. They must never expose raw exceptions, stack traces, tokens, or protocol payloads.
- `dynamic` is permitted only inside the smallest protocol decoding function when required by the decoder. Validate and convert it immediately into typed values; it must not cross into domain, state, or presentation code.
- Use the null assertion operator (`!`) only when an immediately visible check or constructor contract establishes the invariant; otherwise handle null explicitly.

## Documentation

Document non-obvious decisions and compatibility constraints at the boundary where they matter. Do not add comments that merely restate the code.

- Add concise `///` documentation to every public class, entity, model, typedef, constructor, property, factory, and method.
- Describe purpose and contract, not implementation; include unavailable, nullable, unit, lifecycle, or compatibility meaning when relevant.
- Use Dart doc links such as `[CharacterStateEntity]` and `[toJson]` when referring to another documented symbol.
- Private helpers need documentation when their validation, ownership, or compatibility behavior is not obvious from their name.
