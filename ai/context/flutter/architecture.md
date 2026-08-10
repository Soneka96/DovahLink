# Flutter architecture

These conventions apply to the DovahLink Flutter client. They are adapted from the Price check project and are local to this repository.

## Layer direction

```text
data → domain ← presentation
presentation → domain
```

- `data` performs external I/O and maps models to domain entities.
- `domain` contains pure Dart entities, repository interfaces, and use cases.
- `presentation` contains screens, sections, widgets, view models, and client state.
- Domain must not import Flutter or infrastructure packages.
- Presentation must not call datasources, repositories, or transport clients directly.

## Feature structure

```text
lib/
  features/<feature>/
    data/
      datasources/
      models/
      repositories/
    domain/
      entities/
      repositories/
      usecases/
        params/
    presentation/
      screens/
      widgets/
      state/
  shared/
```

Do not pre-create empty `data`, `domain`, or `presentation` subfolders. Add a folder when the first file that belongs there exists.

## Feature boundaries

- Put code in an existing feature when its purpose clearly belongs there.
- Put only feature-neutral plumbing in `shared/`.
- If placement between an existing feature and shared code is ambiguous, ask before creating the file.
- Do not create a generic service to hide one feature's I/O.

## File rules

- One class per file.
- The only exception is a `StatefulWidget` and its paired `State<T>` class.
- Keep use cases to one public operation.
- Keep view models as thin presentation connectors; business logic belongs in domain or state logic.
- Keep Flutter and DovahLink protocol types separate. Map protocol DTOs at the client boundary.

## Navigation and state

- Widgets must not call the router directly; use the project's navigation service.
- Route paths belong in named route constants, never inline strings.
- Keep purely local presentation state local to the widget.
- Use shared state only when another screen, use case, or process needs the value.
- Do not choose a state-management package until the maintainer approves it for DovahLink.

## Visual rules

- Check approved DovahLink design references before making a new visual decision.
- Do not hardcode colors, typography, spacing, icon sizes, or corner radii inside widgets once the theme system exists.
- Use the existing theme and layout tokens; add a new token before adding a repeated literal.
