# Python style

These conventions apply to handwritten Python repository tooling and its tests.

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
