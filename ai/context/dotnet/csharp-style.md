# C# style

These conventions apply to handwritten C# in integration clients, tests, and repository tooling.

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
