# Python style

These conventions apply to handwritten Python repository tooling and its tests.

## Files and organization

One primary public class or top-level function group per file, per `ai/context/common.md`'s shared
file-organization rule. Its two exceptions apply per tool/script (each standalone `tooling/*.py`
script or package gets its own, never shared across scripts): every enum belongs in that tool's
`enums.py`, and every small cross-cutting constant value (timeouts, limits, and similar) belongs in
that tool's `constants.py`. Within either file, group entries by the area they belong to, each
preceded by a `# ---- <Area> ----` comment banner.

A small result/outcome class used by only one other type is not a third exception: it still gets
its own module rather than being defined inside the module of the type that returns it, per
`ai/context/common.md`'s file-organization rule.

## Documentation

Follow the shared documentation rules in `ai/context/common.md` and PEP 257 placement.

- Give every handwritten module, class, method, property, and named function a docstring, regardless
  of visibility. This includes private helpers and test helpers. Document class attributes and enum
  members with a concise `#` comment directly above the assignment because Python does not attach
  docstrings to those declarations.
- Place a module docstring before imports and a class or function docstring as the first statement
  in its body. Decorators remain immediately above the declaration as required by Python syntax.
- Use a one-line docstring when it completely describes a simple helper. Use a summary followed by
  Google-style `Args:`, `Returns:`, and `Raises:` sections only when those sections add contract
  information.
- Keep type information in annotations. Do not repeat annotated types in docstring parameter or
  return descriptions.
- Use Sphinx roles such as `:class:`, `:meth:`, and `:func:` only when a rendered symbol link is
  useful; otherwise use the symbol name in backticks.
- Keep docstring indentation consistent with the declaration and leave blank docstring lines empty,
  without trailing spaces or tabs.
- Use `#` comments inside a function for implementation reasoning that does not belong to the
  callable's contract.
