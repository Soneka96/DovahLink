# Common conventions

These conventions define versioning, repository ownership, and quality rules shared by the Flutter
client, SKSE bridge, protocol, and integration work. AI authority belongs to `AGENTS.md`; branch and
pull request workflow belongs to `CONTRIBUTING.md`.

## Versioning

- Roadmap phase numbers and application versions are separate; completing a phase does not require
  matching the application version to that phase number.
- Flipping a completed phase's `**Status:**` line to Complete in `ROADMAP.md`/`roadmap/*.md` is part
  of the pull request that completes that phase's work, in the same branch, since it's small and
  tied directly to what that PR did. Fix any repository-consistency check
  (`tooling/test_repository_consistency.py`) that a hardcoded expectation now needs updating for as
  part of that same PR.
- The version bump, its `CHANGELOG.md` entry, and syncing every hand-maintained version literal
  (`bridge/vcpkg.json`, the Bridge/.NET/Dart literals and fixtures `tooling/test_repository_consistency.py`'s
  `test_bridge_version_literals_match_the_published_release` enumerates, and that same file's
  `CHANGELOG.md`-version bookkeeping) are their own dedicated release branch and release-only pull
  request, never bundled into a feature/phase branch: that sync already touches over a dozen files
  across every language in the repo on its own, and folding it into an already-large feature PR
  makes that PR harder to review for no benefit. Cut the release branch from `main` once the
  phase(s) it covers are merged; it can cover one phase's completion or several unreleased ones at
  once.
- Cutting a release is a distinct, later, manual step performed after a version-bumped release
  branch has merged into `main`: building the versioned Bridge ZIP with `tooling/BridgeBuilder` and
  uploading it to Nexus Mods (see that tool's own README). `CHANGELOG.md` entries are written at
  release-branch merge time as described above, independent of when the corresponding release is
  actually cut. A merged, version-bumped change can sit unreleased for an arbitrary time -- for
  example while `main` is blocked from publishing at all, per "Pre-release compatibility" below --
  without that blocking further phase or version-bump merges.
- Do not add any other changelog, release artifact, or release automation beyond this without a
  maintainer decision.

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

## Behavioral boundaries and test isolation

- Every behavior-bearing class or equivalent type has an explicit interface, port, protocol,
  abstract base, or other language-appropriate contract, even when it has only one implementation
  today. Every consumer depends on that contract rather than the concrete implementation.
- Every behavior-bearing implementation receives every collaborator through constructor injection
  or the language's direct equivalent. Production code must not construct or resolve a collaborator
  internally through a service locator, hidden singleton, or private dependency factory.
- Every behavior-bearing implementation has exactly one project-owned contract. A project-owned
  contract must not inherit from, extend, or combine another project-owned contract. When a
  consumer needs multiple capabilities, define a focused capability class or a separate adapter;
  do not accumulate interfaces on one implementation or create an interface hierarchy to reuse
  method declarations. Framework-required base classes are not project-owned contracts and are the
  only inheritance exception to this rule.
- This rule is phased: it applies to the language or area when its approved implementation work
  begins. A
  later convention change does not reopen a completed phase or enlarge an already-open pull request;
  a future phase must not introduce new behavior-bearing concrete boundaries without their contract.
- A phase begins when its approved implementation work starts, not when the phase is merely listed
  or discussed in a plan. A later phase that consumes a grandfathered concrete type must introduce
  the contract at its own boundary; it must not reopen the completed phase solely to retrofit it.
- Behavior-bearing means a type that owns decisions, validation, state transitions, I/O, side
  effects, lifecycle, or coordination. A state machine, adapter, validator, codec, or leaf policy
  is not exempt merely because it has one caller or no current collaborator.
- Pure functions, DTOs, protocol/value objects, enums, and other data-only types do not receive
  artificial interfaces merely to satisfy this rule.
- Composition roots may construct concrete implementations in order to pass them into contracts;
  framework-managed entry points may resolve already-registered contracts from the framework
  container, and framework-required callback registration is boundary wiring. Neither is a
  substitute for constructor injection inside ordinary behavior-bearing classes; implementations
  must never be constructed by those lookups or callbacks. Any other exemption or grandfathering
  must be recorded at the relevant phase boundary.
- A language or area convention may enumerate a typed lifecycle-inversion callback as a narrow
  exception only when a real construction cycle prevents constructor injection. The composition
  root assigns it explicitly, and the callback must not construct or resolve implementations.
- Consumer tests prove the consumer's behavior through test doubles for behavior-bearing
  collaborators, including calls, arguments, failure handling, and contractually important
  ordering.
- Choose the test double from the behavior under test, not from the language or package. Use a
  strict mock for synchronous, stateless interaction-only behavior. Use a controllable,
  thread-safe fake when timing, lifetime, cross-thread access, synchronization, or mutable state
  is part of the behavior being controlled or asserted. Area conventions may require a stricter
  choice, but must not weaken this rule; framework-specific mock guidance does not replace it.
- When a consumer changes to depend on a new contract, its isolated consumer test changes in the
  same implementation step. Real collaborator composition belongs only in a small explicit
  composition test.
- A collaborator's behavior remains owned by that collaborator's own tests. Do not mock DTOs,
  value objects, pure conversion functions, or other logic whose behavior is the subject of the
  test.
- Small composition or integration tests use real implementations to prove that the production
  graph is wired correctly; they are not a second exhaustive suite for every collaborator.

## File organization

Keep one primary public type (class, struct, record, interface, enum, or similar) per file across
every language in this repository. A DovahLink interface and its one concrete implementation are
the one additional paired exception: they share the implementation's owning file and no unrelated
public type may be placed there. Structs, result types, and value types each own their own file.
No interface/value pair or collection of small types may share a file merely because the declarations
are related or short. Two narrow exceptions exist because they would otherwise scatter many
closely-related declarations across a large number of near-empty files: enums, and small
cross-cutting constant values (timeouts, limits, and similar non-secret fixed values). Each language area groups these into its own shared file(s) within one compilation
unit/package/project -- not across a package or project boundary, and not one repository-wide
dumping ground -- ordered and separated into clearly labeled groups rather than left as a flat
unordered list. Every other rule (documentation, naming, layering) still applies inside these files
exactly as it would to any other file. The exact file name and grouping convention is area-specific;
see:

- Dart (Flutter client and SDK): `ai/context/dart/dart-style.md`
- C++ (Skyrim bridge): `ai/context/skse/cpp-style.md`
- C# (.NET validation client and tooling): `ai/context/dotnet/csharp-style.md`
- Python (repository tooling): `ai/context/python/python-style.md`

A small type that exists only to be returned by, or passed to, exactly one other type -- a
result/outcome value type, for example -- is not a third exception alongside enums and constants:
it still gets its own file under the one-type-per-file default above, the same as any other primary
type, rather than being nested inside the type it serves. This applies uniformly across languages.
The one narrow carve-out is a type that is structurally inseparable from its owner rather than
merely convenient to nest beside it -- for example a C++ RAII helper that requires `friend` access
to its owner's private state, or an equivalent language-specific coupling that cannot be expressed
across a file boundary. Document a carve-out type as such where it is declared; do not invoke it
merely because a type is small or currently used in only one place.

A curated public export barrel (for example a Dart package's `lib/<package>.dart`) is not subject
to this rule: it declares no types of its own, it only re-exports a curated public surface that
already lives in its own properly organized file elsewhere, per each area's own public-API
conventions (for the SDK, `ai/context/sdk/api-design.md`'s "curated public exports").

## Addition convention

When extending an ordered collection or section -- enums, class members, changelog entries,
configuration lists, and similar -- add new items to the **end** of their respective section, not
the beginning. This preserves line-number stability for existing entries and prevents constant
drift when reviewing or auditing changes. Applied uniformly:

- Changelog entries: new version sections added after the latest existing release
- Enums: new members added at the end, before the closing brace
- Class/struct members: new fields and methods added at the end
- Configuration sections: new entries added after existing entries in that section
- Any ordered list: new items at the end, not beginning

This rule prioritizes stability over reverse-chronological display: existing entries remain at
stable line numbers across the file's history.

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
- Do not tag a comment with the name of an AI assistant, editor plugin, or other authoring tool
  (for example a `ponytail:` prefix marking a deliberate simplification). `AGENTS.md`'s
  "self-contained" requirement applies to documentation vocabulary, not only to code and
  dependencies; a marker meaningless outside one session's tooling is not a description of the
  code. State the same tradeoff in plain language instead -- what was simplified, why it is
  accepted, and what would change the answer (a neutral lead-in such as "Known limitation:" reads
  the same to every future reader).
- Do not cross-reference a roadmap, stage, planning, or other documentation file from
  implementation code, tests, or implementation comments as the authority for behavior. State the
  invariant, behavior, or constraint itself in plain language instead. Documentation may link to
  another document for navigation, but durable implementation rules belong in the relevant context
  file and code comments must remain valid if planning documents are renamed or removed.

## Quality floor

- Keep failure behavior explicit and understandable.
- Do not hide stale, missing, or incompatible data behind plausible defaults.
- Keep read-only behavior as the default until an action has an approved safety model.
- Do not introduce a second implementation of a rule that belongs in a shared contract.
- Do not introduce deprecated or end-of-life dependencies, tools, runtimes, action versions, or APIs.
- Pin every GitHub Actions `uses:` reference to the full immutable commit SHA of the intended release, with the corresponding human-readable version kept in an adjacent comment (for example `# v5`); a version bump must replace the SHA and the comment together, and never use a floating branch such as `@main` or a floating version tag such as `@v5` for workflow dependencies.
- If a maintained action has no stable replacement for a deprecated runtime, keep the current stable release only with a nearby workflow comment explaining the exception and review it when an upstream replacement is published.

## Domain modeling

These principles counter a specific failure mode: the quality floor above is good at preventing
overengineering, but that same pressure can push a change toward the smallest patch instead of a
representation that actually matches the domain. Apply these alongside the quality floor, not
instead of it.

- Model the complete current domain. Do not collapse already-distinct states into a simpler
  representation merely because it makes the patch smaller; a smaller diff that loses a real
  distinction the domain has today is not the smaller change, it is a different, wrong one.
- Lifecycle-coherent state belongs together. State that forms one invariant and is changed or
  observed together should generally be represented as one coherent domain object rather than
  several independently-tracked parallel fields, so it either exists as one complete, coherent
  record or does not exist -- never a state where one field is set while a related one is absent.
- Concurrent decisions require coherent snapshots. Do not let a caller make one decision by
  combining several independently synchronized getters; when a decision needs a consistent view of
  mutable state, read that state once, under one lock, as one snapshot.
- Security-significant state must be explicit. Authentication, authorization, trust, privilege, and
  provenance must not receive convenience defaults where omitting them would silently select
  meaningful behavior; require the caller to state them.
- These rules do not justify speculative abstraction. Model what exists in the domain today
  correctly; do not build for a hypothetical future requirement.

## Pre-release compatibility

DovahLink has no supported public release yet. The previous Nexus listing was removed because the
Skyrim component required the DovahLink Companion App, which had no public way to be downloaded.

- Until a usable Companion App is publicly downloadable, do not publish or re-publish DovahLink on
  Nexus as a public, usable release. Private and development testing is unaffected.
- Until DovahLink has its first supported public release, no previous PR, branch, local build, test
  build, or other unreleased version is a compatibility target. Do not preserve an API, protocol,
  data format, or behavior solely because an older unreleased implementation used it, and do not add
  a compatibility shim, deprecated alias, migration, fallback protocol, or convenience default
  merely to keep an unreleased version working. Prefer the cleanest current architecture, and update
  Bridge, SDK, app, tests, and docs together when a contract changes.
- "Previous DovahLink versions need to keep working" is not a valid justification on its own unless
  a genuine supported public release already exists.
- Once the first supported public release ships, this section's rule no longer applies as written;
  define the real compatibility and versioning policy for the project at that point.
