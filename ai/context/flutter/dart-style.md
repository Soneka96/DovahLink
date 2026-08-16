# Flutter and Dart style

Shared Dart-language conventions (type safety, naming case, formatting, async, dartdoc mechanics)
live in [`ai/context/dart/dart-style.md`](../dart/dart-style.md). This file covers Flutter- and
DovahLink-app-specific conventions only.

## Naming and files

- Use the naming convention established for the relevant type before adding a file. When no existing example exists, follow `ai/context/dart/dart-style.md`'s baseline naming rules, plus descriptive protocol suffixes such as `CharacterStateModel` and `CharacterStateMessage`.
- Name files after the concept they contain, not after the screen that happens to use them.
- Keep protocol mapping names explicit so a Flutter model is not confused with a wire message.

## Documentation

Follow the shared documentation rules in `ai/context/common.md` and the Dart-doc-comment mechanics
in `ai/context/dart/dart-style.md`.

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
