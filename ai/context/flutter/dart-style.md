# Flutter and Dart style

## Type safety

- Do not use `dynamic` or `any`-style escape hatches to avoid modelling a type.
- `dynamic` is permitted only inside the smallest protocol decoding function when required by the
  decoder. Validate and convert it immediately into typed values; it must not cross into domain,
  state, or presentation code.
- Use the null assertion operator (`!`) only when an immediately visible check or constructor
  contract establishes the invariant; otherwise handle null explicitly.

## Naming and files

- Use the naming convention established for the relevant type before adding a file. When no existing example exists, use Dart defaults: `snake_case.dart` filenames, `UpperCamelCase` types, `lowerCamelCase` members, and descriptive protocol suffixes such as `CharacterStateModel` and `CharacterStateMessage`.
- Name files after the concept they contain, not after the screen that happens to use them.
- Keep protocol mapping names explicit so a Flutter model is not confused with a wire message.

## Documentation

Follow the shared documentation rules in `ai/context/common.md`.

- Add concise `///` documentation directly above every handwritten class, extension, enum and enum
  member, typedef, constructor, property, field, parameter field, factory, method, and function,
  regardless of visibility. This includes private helpers and test helpers.
- Use Dart doc links such as `[CharacterStateEntity]` when referring to another documented symbol,
  and fully qualify member links such as `[CharacterStateModel.toJson]`. Add the declaring import
  even when the link is its only reference.
- Describe dependencies in the architectural direction: Model to Entity, UseCase to repository
  interface, and repository implementation to repository interface. Domain never imports data.
- A model and its sole entity or a ViewModel and its sole screen may cross-reference each other when
  the pairing is explicit and exclusive. Do not name other consumers.
- When an override keeps the inherited contract unchanged, use a short doc reference to the
  overridden member instead of copying its documentation.
- Missing implementation uses `// TODO: ...` immediately above the declaration. Explanatory
  implementation comments remain inside the method beside the decision they explain.

## Baseline Dart rules

- Do not add a dependency when the existing SDK or project code is sufficient.
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

## Logging and Flutter-specific rules

- Never use `print`, `debugPrint`, `developer.log`, direct console writes, or ad-hoc logging
  packages. Until an approved logger exists, diagnostics use typed results or state. Never log
  credentials, tokens, raw protocol payloads, or unredacted game state.
- Prefer `StatelessWidget` over helper methods returning widgets; extract reusable helpers into
  widgets that can be tested and made `const`.
- Localize `setState` to the smallest subtree, avoid expensive work in `build`, and split widgets
  at change boundaries.
- Use `ListView.builder` for long or data-driven lists.
- Prefer `AnimatedOpacity` or semitransparent colors over `Opacity` in animations, and avoid
  unnecessary clipping when `borderRadius` is sufficient.
