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
- Every finite canonical wire vocabulary, including protocol message types and error codes, is a
  typed enum. Decode it at the DTO boundary with `@JsonValue`; an unrecognized value is a typed,
  fail-closed protocol-format failure. A compatible Bridge/SDK pair must never rely on raw wire
  strings for branching.

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

Private fields remain the normal way to express internal state and invariants. Avoid private methods:
a method with its own responsibility -- transforming data, generating a value, performing I/O,
coordinating collaborators, classifying an error, or containing independently testable control flow
-- belongs in a named class with a clear responsibility in its own file. A private method is allowed
only when it is a small, inseparable expression of its owner's invariant and has no independent
responsibility. Avoid generic `Helper`/`Utils` names; name a class after the responsibility it owns.
Flutter Redux action handlers required by `ai/context/flutter/architecture.md` are the narrow
framework-specific exception.

Keep handwritten `fromJson`/`fromMap` factories as small decoding boundaries: decode the generated
or primitive representation, invoke semantic validation, return the typed value, and translate
errors at the boundary. When a decoder contains several independent semantic rule families, move
those responsibilities into named, independently testable types in their own files rather than
letting the factory become a long sequence of unrelated branches or generic helper calls. Each
extracted type must own a coherent domain responsibility, not merely split one method's branches
into private methods, and its direct behavior belongs in that type's owning test file. DTO tests
cover decoding, boundary error translation, and composition with the validator. A single small
invariant may remain inline. Generated JSON adapters decode DTO shape; they do not replace
handwritten semantic validation at the DTO/protocol boundary, and protocol semantics must not be
delegated to domain or service layers.

## Member declaration order

Within a class, declare static constants first, then `final` dependencies supplied by a constructor,
then constructors and factories, then internally-created or mutable private state, public getters,
public methods, and the remaining permitted private operations. This makes a type's dependencies
visible before the constructor that wires them, while keeping state that does not come from callers
next to the behavior that owns it.

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

## Test organization

- Group tests by the one callable entry point or one clearly defined behavior under test, never by
  the containing class. A method group is named `Method <methodName> behaves correctly` and does
  not include the class name. Tests for actions, selectors, reducers, middleware, widgets, and
  protocol fixtures target the specific action, selector, entry point, widget behavior, or fixture
  behavior being proven rather than the whole type.
- Keep every test inside one group and do not nest groups. Test descriptions name the method,
  action, selector, or behavior and state the expected result or side effect.
- Place top-level test helpers and fixture builders before `main()`. Keep a helper local to
  `main()` only when it requires per-file state declared there; otherwise lift it to the top level
  and pass its dependencies explicitly.
- A fixture builder is a top-level `build<Type>({...})` function with named parameters and
  representative defaults; do not expose a shared mutable value. A focused shared fixture builder
  owns the primary construction of the repeated test entity, while a test-local fixture may compose
  it and override only the fields relevant to its scenario. Direct entity construction remains
  appropriate when the exact malformed or boundary shape is itself the behavior under test.

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
- Inside `lib/`, never use relative imports; import repository code through its owning package URI.
  `tidy_imports` owns import grouping and ordering: run `dart run tidy_imports` from the package
  root after changing imports, and use `dart run tidy_imports --exit-if-changed` for verification.
  It orders `dart:`, Flutter, third-party package, and own-package imports deterministically.
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
