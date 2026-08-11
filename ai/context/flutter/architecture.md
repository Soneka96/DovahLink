# Flutter architecture

These conventions apply to the DovahLink Flutter client. They are adapted from the Price check project and are local to this repository.

## Layer direction

```text
data -> domain <- presentation
presentation -> domain
```

- `data` performs external I/O and maps models to domain entities.
- `domain` contains pure Dart entities, repository interfaces, and use cases.
- `presentation` contains screens, sections, widgets, view models, and client state.
- Domain must not import Flutter or infrastructure packages.
- Presentation must not call datasources, repositories, or transport clients directly.
- Imports may point from `presentation` to `domain`, and from `data` to `domain`.
- Domain must not import `data`, `presentation`, Flutter, or transport implementations.
- Presentation may consume domain interfaces and client-state outputs, but never construct infrastructure.
- Infrastructure implementations, protocol clients, repositories, and their dependencies may only be constructed in the approved application composition root. Features receive abstractions through constructors or approved dependency injection. Do not create service locators or hidden global singletons.

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
- `shared/` is for code used by at least two real features or for approved application-wide infrastructure.
- Do not move feature code into `shared/` merely to avoid choosing an owner.

## File rules

- One public class or model per file.
- Every data model has a corresponding domain entity in `domain/entities/`, and the model extends that entity. Models own serialization; entities remain Flutter- and infrastructure-independent.
- Actions are the only permitted multi-class file exception, because action declarations and their closely related action value types are intentionally grouped.
- A `StatefulWidget` and its paired `State<T>` class may also share a file.
- Keep use cases to one public operation.
- Keep view models as thin presentation connectors; business logic belongs in domain or state logic.
- Keep Flutter and DovahLink protocol types separate. Map protocol DTOs at the client boundary.
- Protocol DTOs must not cross into widgets or domain entities.
- Protocol wire DTOs, encoding/decoding, session validation, and transport-facing adapters belong in the feature's `data` boundary or an explicitly approved client-infrastructure area. Convert them to Flutter-facing models before they enter domain or presentation code.

### JSON models and generated code

- Use `json_serializable` for Dart models that map to or from JSON; do not hand-write repetitive `fromJson`/`toJson` mappings for protocol DTOs.
- Run generation with `dart run build_runner build` from the owning Flutter project.
- Generated `.g.dart` files belong beside the source model in the owning area and must never be hand-edited.
- `json_serializable` is a mapping tool, not the protocol contract. The canonical schema remains in `protocol/schema/`, and shared examples remain in `protocol/fixtures/`.
- Keep semantic validation outside generated code: protocol versions, revisions, message/payload pairing, session identity, finite values, security limits, and recovery rules must be validated by handwritten boundary code.
- Configure generated models to preserve required-versus-unavailable distinctions; a missing required field must not silently become `null`.

## Navigation and state

- Widgets must not call a router or navigation package directly. Use the approved navigation boundary once one exists; until then, do not invent a navigation service or package.
- Until navigation is approved, do not add routing infrastructure or route constants. Once approved, route paths belong only in the approved navigation boundary and must never be repeated as inline strings.
- Keep purely local presentation state local to the widget.
- Use shared state only when another screen, use case, or process needs the value.
- Do not choose a state-management package until the maintainer approves it for DovahLink.

### Connection and recovery state

- Keep connection/session state separate from feature display state.
- A newly accepted session invalidates all messages and subscriptions from the previous session.
- A revision gap, local outbound queue loss, reconnect, or protocol recovery request enters `recovering`; treat local queue loss as an internal client-state signal unless the canonical protocol schema explicitly defines a corresponding wire message. Do not invent wire messages in Flutter code.
- A snapshot is valid only when its session ID matches the active session, its correlation matches the current recovery request when applicable, and its state-area revision is accepted by the canonical protocol rules.
- Validate session identity, message correlation, and per-area revisions in the protocol/client-state boundary, not in widgets.
- Serialize transitions by session generation and state area; a session replacement always wins over queued or in-flight messages from the previous generation, even when the older message has a higher revision.
- Every subscription, timer, request, and stream completion owned by a disposable client-state or screen owner must be cancellable or guarded by an active-generation/disposed check before mutating state.

## Visual rules

- Check approved DovahLink design references before making a new visual decision. If no local reference or design system exists, record the decision and do not import an external design system without approval.
- Build the approved Skyrim-inspired presentation with native Flutter theming and components.
- Keep fonts, colors, panels, icons, spacing, and animations behind shared theme tokens or themed components so the Core UI Theme System can support future adapters.
- Do not hardcode colors, typography, spacing, icon sizes, or corner radii inside widgets once the theme system exists.
- Use the existing theme and layout tokens; add a new token before adding a repeated literal.
- Keep the native DovahLink theme complete and usable without installed-resource detection or a UI mod adapter; missing or unsupported adapter values must fall back to it.
