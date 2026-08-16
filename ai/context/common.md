# Common conventions

These conventions define versioning, repository ownership, and quality rules shared by the Flutter
client, SKSE bridge, protocol, and integration work. AI authority belongs to `AGENTS.md`; branch and
pull request workflow belongs to `CONTRIBUTING.md`.

## Versioning

- Roadmap phase numbers and application versions are separate; completing a phase does not require
  matching the application version to that phase number.
- A release is cut by building the versioned Bridge ZIP with `tooling/BridgeBuilder` and uploading
  it to Nexus Mods manually. `CHANGELOG.md` at the repository root is the developer-facing record of
  what changed in each release; update it in the same change that bumps `bridge/vcpkg.json`'s
  `version-string` and flips the corresponding `ROADMAP.md` phase to Complete. Do not add any other
  changelog, release artifact, or release automation beyond this without a maintainer decision.

## Repository boundaries

- `app/` is reserved for the Flutter client.
- `bridge/` is reserved for the native SKSE bridge.
- `protocol/` is the sole home for canonical cross-side schemas and shared protocol fixtures.
- `sdk/` is reserved for reusable, supported client SDK implementations; see `sdk/README.md`.
- `integration/` is reserved for tests and scenarios that exercise boundaries between areas.
- `test/` is reserved for tests owned by an implementation area; cross-side contract fixtures remain in `protocol/fixtures/`.
- `tooling/` is reserved for maintainer-approved repository scripts and validation tools.
- Root-level configuration belongs only to repository-wide tooling; it must not become a dumping ground for implementation code.
- Generated files belong in the area that owns their source and must never be hand-edited.
- C++ conventions are defined in `ai/context/skse/cpp-style.md`.
- Shared Dart-language conventions are the complete set in `ai/context/dart/dart-style.md`; Flutter
  conventions are the complete set in `ai/context/flutter/architecture.md`, `dart-style.md`,
  `testing.md`, and `error-handling.md`; SDK conventions are the complete set in
  `ai/context/sdk/architecture.md`, `api-design.md`, `persistence.md`, and `testing.md`. Do not
  replace any of these with a summary, and do not duplicate a rule across more than one of them.
- C# conventions are defined in `ai/context/dotnet/csharp-style.md`.
- Python conventions are defined in `ai/context/python/python-style.md`.
- No area may place its implementation types, private fixtures, or infrastructure in another area's directory.
- `protocol/fixtures/` contains canonical cross-side fixtures; `integration/` contains scenarios and harnesses that consume them.

## File organization

Keep one primary public type (class, struct, record, interface, enum, or similar) per file as the
default, across every language in this repository. Two narrow exceptions exist because they would
otherwise scatter many tiny, closely-related declarations across a large number of near-empty
files: enums, and small cross-cutting constant values (timeouts, limits, and similar non-secret
fixed values). Each language area groups these into its own shared file(s) within one compilation
unit/package/project -- not across a package or project boundary, and not one repository-wide
dumping ground -- ordered and separated into clearly labeled groups rather than left as a flat
unordered list. Every other rule (documentation, naming, layering) still applies inside these files
exactly as it would to any other file. The exact file name and grouping convention is area-specific;
see:

- Dart (Flutter client and SDK): `ai/context/dart/dart-style.md`
- C++ (Skyrim bridge): `ai/context/skse/cpp-style.md`
- C# (.NET validation client and tooling): `ai/context/dotnet/csharp-style.md`
- Python (repository tooling): `ai/context/python/python-style.md`

## Documentation

Documentation describes the purpose and contract of the code it is attached to. Apply these rules
consistently across languages, using the syntax and placement defined by the relevant language
style guide.

- Document every handwritten named type, enum and enum member, constructor, property, field,
  method, and function, including private methods and test helpers. A single sentence is enough
  when the contract is simple.
- Place documentation directly on the declaration it describes. Language-required attributes,
  metadata, and decorators may appear between documentation and the declaration. Python docstrings
  are the syntax-required exception: they are the first statement inside the documented module,
  class, or function.
- Describe purpose and contract rather than restating the declaration. Include relevant ownership,
  lifetime, thread-safety, side effects, nullability or unavailable-state meaning, units, failure
  behavior, security constraints, and compatibility requirements.
- Documentation may name a dependency in the forward direction when the relationship explains the
  contract: a method may say that it uses, delegates to, or converts through another method or
  type. Do not turn documentation into a list of internal calls.
- Never list the callers or consumers of a type or method. Such lists duplicate the call graph and
  become stale as consumers change.
- A declaration may name its sole paired consumer when the relationship is intentionally
  one-to-one, exclusive, stable, and part of the documented architecture. Examples include a model
  paired with its entity or a ViewModel paired with its screen. Describe the relationship itself
  rather than incidental call sites.
- Use symbol-aware links provided by the language when referring to code. Use repository-relative
  paths when referring to project documents. Do not refer to temporary task files, implementation
  steps, review comments, or current call-site inventories.
- Implementation comments inside a method explain why a decision, workaround, ordering constraint,
  or safety measure exists. Do not narrate what the next statement already says.
- Reuse inherited documentation when the language supports it and the inherited contract is
  unchanged. Do not copy documentation that can drift from its source.
- Generated code is excluded because it must not be hand-edited. Documentation coverage targets
  are checks on the intended convention, not a reason to add inaccurate or repetitive prose.

## Quality floor

- Keep failure behavior explicit and understandable.
- Do not hide stale, missing, or incompatible data behind plausible defaults.
- Keep read-only behavior as the default until an action has an approved safety model.
- Do not introduce a second implementation of a rule that belongs in a shared contract.
- Do not introduce deprecated or end-of-life dependencies, tools, runtimes, action versions, or APIs.
- Prefer maintained stable releases and pinned action versions; never use floating branches such as `@main` for workflow dependencies.
- If a maintained action has no stable replacement for a deprecated runtime, keep the current stable release only with a nearby workflow comment explaining the exception and review it when an upstream replacement is published.
