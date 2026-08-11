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
- Imports may point from `presentation` to `domain`, and from `data` to `domain`.
- Domain must not import `data`, `presentation`, Flutter, or transport implementations.
- Presentation may consume domain interfaces and client-state outputs, but never construct infrastructure.
- Domain dependencies are constructor-injected interfaces. Domain code never imports or resolves
  the `GetIt` container.

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
    constants/
    failures/
    navigation/
    state/
    theme/
    usecase/
  injection_container.dart
  main.dart
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
- Use these suffixes: `.widget.dart`, `.section.dart`, `.screen.dart`, `.usecase.dart`,
  `.params.dart`, `.entity.dart`, `.model.dart`, `.actions.dart`, `.middleware.dart`,
  `.reducer.dart`, `.selectors.dart`, `.state.dart`, `.viewmodel.dart`, `.repository.dart`, and
  `_remote.datasource.dart`/`_local.datasource.dart`.
- Use `feature.repository.dart` for implementations and `Ifeature.repository.dart` for domain
  interfaces. Keep use-case params in `domain/usecases/params/`, never in the use-case file.
- Every data model has a corresponding domain entity in `domain/entities/`, and the model extends that entity. Models own serialization; entities remain Flutter- and infrastructure-independent.
- When a generated JSON model extends a concrete entity and needs typed model fields for nested
  serialization, its constructor forwards those fields through an explicit `super(...)` initializer
  (for example, `: super(level: level, health: health, ...)`). This is intentional model-boundary
  boilerplate required by `json_serializable`; it is not a general constructor pattern.
- Actions are the only permitted multi-class file exception, because action declarations and their closely related action value types are intentionally grouped.
- A `StatefulWidget` and its paired `State<T>` class may also share a file.
- Keep use cases to one public operation.
- Keep view models as thin presentation connectors; business logic belongs in domain or state logic.
- Keep Flutter and DovahLink protocol types separate. Map protocol DTOs at the client boundary.
- Protocol DTOs must not cross into widgets or domain entities.
- Protocol wire DTOs, encoding/decoding, session validation, and transport-facing adapters belong in the feature's `data` boundary or an explicitly approved client-infrastructure area. Convert them to Flutter-facing models before they enter domain or presentation code.

## Screens, sections, and widgets

- A Screen is the sole top-level content for its context.
- A Section is bespoke content nested inside a Screen alongside sibling content.
- A Widget is a reusable or repeatable unit with one cohesive purpose.
- Routability and the presence of a ViewModel do not decide the classification.
- Screens and Sections resolve their own DI-registered dependencies; do not thread them through
  constructor props from a parent.
- Reusable widgets receive data and callbacks as props and remain dumb.

## Redux flow

- Use Redux when a value is read by another screen, drives a use case, or must persist beyond one
  widget rebuild. Purely local presentation state stays in the smallest widget's `State`.
- The normal chain is `Screen/Section -> ViewModel -> Action -> Middleware -> ResultAction ->
  Reducer -> AppState -> StoreConnector`.
- Screens and Sections never call `store.dispatch`, use cases, repositories, or services directly.
- ViewModels are thin connectors resolved through DI; they read selectors and create dispatch
  callbacks without re-deriving business state.
- ViewModels and widgets must not read feature fields directly from Redux state. They receive
  feature values through the feature's `.selectors.dart` file; selectors may compose derived
  presentation values from simpler selectors.
- The store is built exactly once through `CreateStore`; every `StoreConnector` uses
  `distinct: true`.
- Reducers use `combineReducers` and typed reducers, never an `if (action is ...)` chain.
- Middleware calls `next(action)` before dispatching handler results so handlers see reduced state.
- Middleware handlers accept only `Store<AppState>` and the typed Action; raw values and
  `BuildContext` are not handler parameters.
- To share handler logic, dispatch a dedicated action rather than calling a raw-parameter helper.

## Feature call chain

Feature business logic follows `Middleware -> UseCase -> Repository -> Datasource`.
Repositories coordinate local and remote datasources; a use case never chooses between them.
One-off I/O belongs to the owning feature datasource, not a generic service.

## Dependency injection

- Register shared dependencies first in `lib/injection_container.dart`, then call each feature's
  injection container.
- Infrastructure implementations, protocol clients, repositories, use cases, services, and ViewModels
  are registered in the manual `GetIt` container.
- Register concrete implementations behind domain interfaces.
- Register use cases as DI dependencies and construct them with repository interfaces.
- Register ViewModels with `registerFactoryParam` when they need a Redux `Store`.
- Screens and Sections resolve registered ViewModels; middleware handlers resolve registered use
  cases and services through `sl<Type>()`.
- Reusable widgets, ViewModels, use cases, entities, repositories, and datasources never resolve
  dependencies from `GetIt`.
- Call dependency initialization once before `runApp`.

## Services

- Do not create a generic feature service layer.
- App-wide plumbing without business rules belongs in `shared/utils/` and is DI-registered; it is
  not wrapped in a use case.
- Feature-specific orchestration stays in its owning feature; do not move it to `shared/utils/` to
  avoid choosing a feature boundary.
- One-off I/O belongs in the owning feature datasource.

## Theming and layout

- Colors and text styles come from `Theme.of(context)` and the approved app theme.
- Spacing and icon sizes come from shared layout constants or theme extensions, never inline widget
  literals once a token exists.
- Corner radii come from button themes or the shared shape theme extension.
- Widgets receive data and callbacks through props; visual styling comes from the theme, not from
  constructor parameters or hidden DI lookups.

## JSON models and generated code

- Use `json_serializable` for Dart models that map to or from JSON; do not hand-write repetitive `fromJson`/`toJson` mappings for protocol DTOs.
- Run generation with `dart run build_runner build` from the owning Flutter project.
- Generated `.g.dart` files belong beside the source model in the owning area and must never be hand-edited.
- `json_serializable` is a mapping tool, not the protocol contract. The canonical schema remains in `protocol/schema/`, and shared examples remain in `protocol/fixtures/`.
- Keep semantic validation outside generated code: protocol versions, revisions, message/payload pairing, session identity, finite values, security limits, and recovery rules must be validated by handwritten boundary code.
- Configure generated models to preserve required-versus-unavailable distinctions; a missing required field must not silently become `null`.

## Client versioning

- The Flutter client version is defined in `app/pubspec.yaml`; platform build metadata is derived
  from it rather than maintained as unrelated manual versions.
- Increment the build number for each distributable client build. Increment the semantic client
  version only for an intentional client release boundary.

## Navigation and state

- Widgets must not call a router or navigation package directly. Use the approved navigation boundary once one exists; until then, do not invent a navigation service or package.
- Until navigation is approved, do not add routing infrastructure or route constants. Once approved, route paths belong only in the approved navigation boundary and must never be repeated as inline strings.
- Keep purely local presentation state local to the widget.
- Use shared state only when another screen, use case, or process needs the value.
- Keep state extraction and derived state logic in selectors. Do not place filtering, mapping,
  fallback selection, or other state-derived decisions in ViewModels or widgets.
- DovahLink uses Redux for shared client state, with the store created once in the application composition root. Features add their reducers and middleware through the approved store-construction boundary.
- Do not introduce another state-management package without maintainer approval.

### Connection and recovery state

- Keep connection/session state separate from feature display state.
- Use explicit states such as `connecting`, `connected`, `recovering`, `disconnected`, `stale`, and
  `failed`; do not collapse distinct protocol states into a generic `loading` flag.
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
