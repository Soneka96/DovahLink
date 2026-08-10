# Flutter and Dart style

## Never do

- Do not use `print()` for application logging; use the approved logging abstraction once one exists.
- Do not use the null assertion operator (`!`) when explicit null handling is possible.
- Do not use `dynamic` or `any`-style escape hatches to avoid modelling a type.
- Do not write snapshot tests.
- Do not add a dependency when the existing SDK or project code is sufficient.

## Naming and files

- Use the naming convention established for the relevant type before adding a file.
- Keep one primary class per file.
- Name files after the concept they contain, not after the screen that happens to use them.
- Keep protocol mapping names explicit so a Flutter model is not confused with a wire message.

## Boundaries

- Keep parsing and serialization at the protocol/client boundary.
- Keep business decisions out of widgets and event callbacks.
- Keep reusable widgets dumb: pass data and callbacks as inputs.
- Do not fetch services or dependencies from a low-level reusable widget.

## Documentation

Document non-obvious decisions and compatibility constraints at the boundary where they matter. Do not add comments that merely restate the code.
