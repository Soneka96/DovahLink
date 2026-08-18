# C# style

These conventions apply to handwritten C# in integration clients, tests, and repository tooling.

## Files and types

One primary public class, record, struct, or interface per file, per `ai/context/common.md`'s
shared file-organization rule. Its two exceptions apply per project (`DovahLinkValidationClient`,
`DovahLinkValidationClient.Tests`, and `tooling/BridgeBuilder` each get their own, never shared
across a project boundary): every enum for that project belongs in that project's `Enums.cs`, and
every small cross-cutting constant value (timeouts, limits, and similar) belongs in that project's
`Constants.cs`. Within either file, group entries by the area they belong to, each preceded by a
`// ---- <Area> ----` comment banner.

A small result/outcome record or class used by only one other type -- for example a typed return
value distinguishing several outcomes of one method -- is not a third exception: it still gets its
own file rather than being nested inside the type that returns it, per `ai/context/common.md`'s
file-organization rule. A nested type is appropriate only when it is structurally inseparable from
its owner (for example it requires `private`-member access no file boundary could express), not
merely because it is small or currently used in one place.

## Documentation

Follow the shared documentation rules in `ai/context/common.md`.

- Use XML `///` documentation directly above every handwritten class, record, struct, interface,
  enum and enum member, delegate, constructor, property, field, event, method, and local function,
  regardless of visibility. This includes private helpers and test helpers.
- Give every declaration a concise `<summary>`, unless it uses `<inheritdoc/>`. Add `<param>`,
  `<typeparam>`, `<returns>`, `<exception>`, and `<remarks>` only when they add useful contract
  information rather than repeat the signature.
- Use `<see cref="..."/>` and `<paramref name="..."/>` for symbol-aware references.
- Use `/// <inheritdoc/>` for interface implementations and overrides whose contract is unchanged.
  Add separate documentation only for behavior introduced by the implementation.
- Place attributes between the documentation and declaration when required; the documentation must
  remain attached to that declaration.
- Use ordinary `//` comments inside a method for implementation reasoning. Do not mix `//` comments
  into an XML documentation block.

## Test fixtures

- Build representative test values through a static `Build<Type>` method with C# optional
  parameters defaulting to one representative value per parameter; a test that wants the default
  calls it with no arguments, and a test that needs one field different overrides only that
  parameter (`BuildPairingHandshake(trusted: false)`).
- Do not export a fixture as a bare `static readonly`/`const` field: a constant cannot be varied per
  test case without either duplicating the whole value under a second name or mutating a shared
  instance, and the builder's own job is to make that variation cheap.
- Group builders in a `Fixtures` class per test project, mirroring this file's `Enums.cs`/
  `Constants.cs` grouping exception above: organized by area with a `// ---- <Area> ----` banner per
  group, rather than scattering ad-hoc test-data construction across individual test files.

## Process execution

- When invoking a script through `cmd.exe`, do not rely on the working directory for command
  lookup. Use an explicit relative path such as `.\script.bat` for a script in the working
  directory, or a validated absolute path.
- Pass dynamic arguments and environment values as structured process data rather than
  interpolating them into shell command text.
