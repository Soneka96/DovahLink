# Dart style

Shared Dart-language conventions that apply to every Dart package in this repository — currently
the Flutter client (`ai/context/flutter/`) and, once it exists, the Dart Client SDK
(`ai/context/sdk/`). Framework-specific and package-specific rules stay in their own convention
area; only genuinely language-wide conventions live here.

## Type safety

- Do not use `dynamic` or `any`-style escape hatches to avoid modelling a type.
- `dynamic` is permitted only inside the smallest protocol decoding function when required by the
  decoder. Validate and convert it immediately into typed values; it must not cross into domain,
  state, or presentation code.
- Use the null assertion operator (`!`) only when an immediately visible check or constructor
  contract establishes the invariant; otherwise handle null explicitly.

## File organization

One primary public class, mixin, or extension type per file, per `ai/context/common.md`'s shared
file-organization rule. Its two exceptions apply per Dart package (the Flutter app and the SDK each
get their own; never one file shared across a package boundary), at each package's own established
location:

- Every enum in the package belongs in that package's `enums.dart`: `lib/shared/constants/enums.dart`
  for the Flutter app, `lib/src/shared/enums.dart` for the SDK.
- Every small cross-cutting constant value (timeouts, limits, and similar) belongs in that
  package's `constants.dart`, alongside its `enums.dart`: `lib/shared/constants/constants.dart` for
  the Flutter app, `lib/src/shared/constants.dart` for the SDK (not yet created; the SDK has no
  cross-cutting constant of this kind yet).

Within either file, group entries by the area they belong to, in the order those areas were
introduced, each preceded by a `// ---- <Area> ----` comment banner. A type that is not itself an
enum but exists only to be returned by, or passed to, exactly one other type (for example a small
result/outcome value class) still gets its own file under the normal one-type-per-file default.

## Class organization

No private named classes in Dart production or test code. The only exceptions are generated JSON
serialization code and private Flutter `State<T>` lifecycle subclasses. ViewModels are allowed only
as named classes in their own files.

This rule governs class-level privacy, not member-level privacy: private fields and methods remain
the normal way to express internal state and invariants within a named class. A behavior or helper
with meaningful logic belongs in a named class with a clear responsibility, in its own file -- never
as a private class inline in the file that uses it, in production code or in a test file. Avoid
generic `Helper`/`Utils` names; name a class after the responsibility it owns.

## Documentation

Follow the shared documentation rules in `ai/context/common.md`.

- Add concise `///` documentation directly above every handwritten class, extension, enum and enum
  member, typedef, constructor, property, field, parameter field, factory, method, and function,
  regardless of visibility. This includes private helpers and test helpers.
- Use Dart doc links such as `[SymbolName]` when referring to another documented symbol, and fully
  qualify member links such as `[SymbolName.member]`. Add the declaring import even when the link is
  its only reference.
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
