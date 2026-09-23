# Flutter and Dart style

Shared Dart-language conventions (type safety, naming case, formatting, async, dartdoc mechanics)
live in [`ai/context/dart/dart-style.md`](../dart/dart-style.md). This file covers Flutter- and
DovahLink-app-specific conventions only.

## Naming and files

- Use the naming convention established for the relevant type before adding a file. When no existing example exists, follow `ai/context/dart/dart-style.md`'s baseline naming rules, plus descriptive protocol suffixes such as `CharacterStateModel` and `CharacterStateMessage`.
- Name files after the concept they contain, not after the screen that happens to use them.
- Keep protocol mapping names explicit so a Flutter model is not confused with a wire message.

## Documentation

Follow the shared documentation rules in `ai/context/common.md` and the Dartdoc symbol-link and
brevity rules in [`ai/context/dart/dart-style.md`](../dart/dart-style.md#documentation). This file
adds Flutter-specific documentation relationships only.

- Describe dependencies in the architectural direction: Model to Entity, UseCase to repository
  interface, and repository implementation to repository interface. Domain never imports data.
- A model and its sole entity or a ViewModel and its sole screen may cross-reference each other when
  the pairing is explicit and exclusive. Do not name other consumers.

## Equatable and value objects

Use `Equatable` for entities, models, value objects, ViewModels, Redux actions, and Redux state.
Do not hand-write equality or hash codes for classes that extend it. Use `Option<T>?` from `fpdart`
for nullable `copyWith` fields so omitted, cleared, and set values remain distinct.

## Enums

- Every enum is declared in `lib/shared/constants/enums.dart`, including feature-local status
  values.
- Give the enum a first `none` member only when the type genuinely has an unselected, unknown, or
  invalid runtime state to represent. `none` is that sentinel, not an app default, and every member
  including it is documented. Omit `none` entirely when every valid instance of the type is
  guaranteed to resolve to one concrete member -- a selectable preset, or a presentation state that
  only exists once its owner exists. Do not add it merely to follow this convention by default, and
  do not model absence, default, or fallback behavior as `none` when the owning boundary (a
  resolver, a persistence read, a factory) already guarantees resolution to a real value.
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
