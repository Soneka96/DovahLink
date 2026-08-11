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

## Baseline Dart rules

- Use UpperCamelCase for classes, enums, typedefs, extensions, and type parameters.
- Use lowerCamelCase for variables, parameters, functions, members, and constants.
- Use lowercase_with_underscores for packages, directories, source files, and import prefixes.
- Capitalize acronyms longer than two letters as words (`HttpRequest`, `Uri`); keep two-letter
  acronyms uppercase (`ID`, `IO`).
- Use non-imperative boolean names such as `isCreating` and `canClose`.
- Never prefix methods with `get`; use a getter or a descriptive verb.
- Use `to___()` to convert to a new object and `as___()` for a view or representation.
- Use `dart format`, trailing commas, braces for flow control, and single quotes.
- Import in groups: `dart:`, package imports, then project imports; alphabetize each group.
- Use explicit types for `final` locals and declare return and parameter types on functions.
- Do not explicitly initialize variables to `null`; avoid `late` unless necessary.
- Prefer interpolation and collection literals; use `.isEmpty`/`.isNotEmpty` instead of length
  comparisons; use `whereType<T>()` instead of `cast()`.
- Use named parameters for functions and constructors with more than two parameters. Avoid
  positional boolean parameters.
- Use `=>` for simple expressions, `;` for empty constructors, and empty collections instead of
  `null` for no data.
- Handle every `Future` error, specify exception types in `catch`, and use `rethrow` to preserve
  stack traces.
- Use `async`/`await` over `.then()` chains and `Future<void>` for async methods without a value.
- Make fields private unless intentionally public and make constructors `const` when possible.

## Equatable and value objects

Use `Equatable` for entities, models, value objects, ViewModels, Redux actions, and Redux state.
Do not hand-write equality or hash codes for classes that extend it. Use `Option<T>?` from `fpdart`
for nullable `copyWith` fields so omitted, cleared, and set values remain distinct.

Generated JSON models that extend concrete entities may use an explicit `super(...)` constructor
initializer to forward their typed model fields. This is required when `json_serializable` must
serialize nested model values, and is intentionally limited to those model constructors.

## Enums

- Every enum is declared in `lib/shared/constants/enums.dart`, including feature-local status
  values.
- The first member is always `none`, a sentinel for an unselected or invalid value—not an app
  default.
- Document every member, including `none`.
- Keep enum extensions directly after their enum in the same file.
- Keep enum behavior that is intrinsic to the enum, such as stable labels or classifications, on
  the enum itself or in its immediately following extension. Test enum methods, factories, and
  extensions when they contain logic.

## Documentation

- Add concise `///` comments to every public class, method, property, constructor, factory,
  typedef, entity, model, parameter field, and enum member.
- Describe what the symbol is and its contract, not surrounding application context or a restatement
  of its implementation.
- Use `[SymbolName]` links in doc comments and add the declaring import even when the link is the
  only reference. Fully qualify member links as `[ClassName.member]`.
- Coupled classes cross-reference in one direction: Model to Entity, UseCase to repository
  interface, and repository implementation to repository interface. Domain never imports data.
- Private non-obvious helpers use a short `//` comment immediately above the declaration. Missing
  implementation uses `// TODO: ...` immediately above the declaration. Do not place explanatory
  comments between statements or widgets.

## Logging and Flutter-specific rules

- Never use `print`, `debugPrint`, or direct console writes. Use the approved logger boundary once
  one exists and never log credentials, tokens, raw protocol payloads, or unredacted game state.
- Prefer `StatelessWidget` over helper methods returning widgets; extract reusable helpers into
  widgets that can be tested and made `const`.
- Localize `setState` to the smallest subtree, avoid expensive work in `build`, and split widgets
  at change boundaries.
- Use `ListView.builder` for long or data-driven lists.
- Prefer `AnimatedOpacity` or semitransparent colors over `Opacity` in animations, and avoid
  unnecessary clipping when `borderRadius` is sufficient.
