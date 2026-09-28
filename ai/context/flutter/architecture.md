# Flutter architecture

These conventions apply to the DovahLink Flutter client. They are adapted from the Price check project and are local to this repository.

## Layer direction

Presentation depends on domain, and data depends on domain. Domain remains independent of
presentation, data, frameworks, and transport implementations.

- `data` performs external I/O and maps external representations to domain entities.
- `domain` contains pure Dart entities, repository interfaces, and use cases.
- `presentation` contains screens, sections, widgets, ViewModels, ViewData, and client state.
- Imports may point from `presentation` to `domain`, and from `data` to `domain`.
- Domain must not import `data`, `presentation`, Flutter, or transport implementations.
- Presentation may consume domain interfaces and client-state outputs, but never construct infrastructure.
- Redux middleware may call a registered SDK contract directly for a complete SDK-owned operation.
  It remains orchestration and does not own protocol rules or application-domain decisions.
- Domain dependencies are constructor-injected interfaces. Domain code never imports or resolves
  the `GetIt` container.

## Domain and presentation values

These four names have separate meanings in the Flutter app:

- **Entity:** A pure Dart domain concept in `domain/entities/`, in a `*.entity.dart` file. Its
  type uses its domain concept's bare name. It has no dependency on Flutter, the SDK, JSON,
  storage, transport, protocol DTOs, `data`, or `presentation`.
- **Model:** A data-layer representation of structured external data in `data/models/`, in a
  `*.model.dart` file. It extends its corresponding Entity and maps an actual representation
  crossing an SDK, API, protocol, JSON, database, or persisted-data boundary.
  The DataSource communicates with the SDK, API, or storage system; the Model is the Flutter
  application's typed representation of structured data crossing that boundary. Domain
  repository interfaces, use cases, Redux state, and presentation code use Entities, not Models.
  A Model exists only when an actual external representation exists; do not manufacture
  Model/Entity pairs merely for structural symmetry. A Model owns external
  mapping or serialization where appropriate.
- **ViewModel:** A presentation-layer Redux adapter named for exactly one Screen, Section, Widget,
  or application presentation owner. It maps Redux state through selectors, creates Redux dispatch
  callbacks, exposes what its owner requires, and contains no domain or business logic. A
  Redux-backed ViewModel is registered through `sl` with `registerFactoryParam`.
- **ViewData:** An immutable presentation-only value in `presentation/viewdata/`, in a
  `*.viewdata.dart` file, passed between UI components. It has no Store, selectors, dispatching, or
  DI registration.

Structured data from an SDK, API, or storage boundary enters through a DataSource and Model before
the corresponding Entity is used by domain logic.

“Model” is reserved for the data layer and must not be used as a generic suffix for immutable
classes. Domain Entity classes use the bare concept name: the `.entity.dart` suffix identifies the
file's architectural role, not an `Entity` suffix on the class. Primitive or enum persistence,
including the existing theme preset setting, does not by itself justify an Entity/Model pair; add
one only for a structured domain concept with an external representation.

## Feature structure

Feature-owned data, domain, and presentation code lives under its feature boundary. Application-wide
plumbing and shared presentation concerns live in the app's shared area.

Do not pre-create empty `data`, `domain`, or `presentation` subfolders. Add a folder when the first file that belongs there exists.

## Feature boundaries

- Put code in an existing feature when its purpose clearly belongs there.
- Put only feature-neutral plumbing in `shared/`.
- If placement between an existing feature and shared code is ambiguous, ask before creating the file.
- Do not create a generic service to hide one feature's I/O.
- `shared/` is for code used by at least two real features or for approved application-wide infrastructure.
- Do not move feature code into `shared/` merely to avoid choosing an owner.

## File rules

- One primary public type per file. Datasource files are the documented exception below: each
  contains both the abstract interface and its concrete implementation.
- Use these suffixes: `.widget.dart`, `.section.dart`, `.screen.dart`, `.usecase.dart`,
  `.params.dart`, `.entity.dart`, `.model.dart`, `.viewdata.dart`, `.actions.dart`,
  `.middleware.dart`, `.reducer.dart`, `.selectors.dart`, `.state.dart`, `.viewmodel.dart`,
  `.repository.dart`, `.datasource.dart`.
- **Datasource files and classes:** Name datasource files `feature_local.datasource.dart` and
  `feature_remote.datasource.dart` (always feature-first, snake_case with underscores). Every
  datasource file requires both an abstract interface and a concrete implementation, per
  `ai/context/dart/dart-style.md`'s "Interface naming": the interface takes the `I`-prefix and the
  implementation keeps the bare capability name, with no implementation suffix. For multi-word
  feature names, use continuous snake_case in filenames and PascalCase in class names. When a
  feature requires multiple local or remote datasources, distinguish their capabilities in the
  datasource name using the same feature-first convention as repositories.
- **Repository files and classes:** the domain interface and its data-layer implementation live in
  different layers, so they cannot share one file the way a datasource pair does -- this is the
  domain/data layer-boundary carve-out `dart-style.md`'s "Interface naming" documents. The
  implementation stays `feature.repository.dart` in `data/repositories/`, holding the bare
  `FeatureRepository` class. The interface lives in `feature_repository.dart` in
  `domain/repositories/` (snake_case, matching the datasource convention above -- never
  `Ifeature.repository.dart`; a Dart filename never takes the `I`-prefix), holding
  `IFeatureRepository`. Keep use-case params in `domain/usecases/params/`, never in the use-case
  file.
- Every Model lives in `data/models/`, has a corresponding Entity in `domain/entities/`, and
  extends that Entity. Create a Model only for an actual structured external representation, not
  merely because an Entity exists. Models own external mapping or serialization where appropriate;
  Entities remain pure Dart and infrastructure-independent.
- When a generated JSON Model extends a concrete Entity and needs typed Model fields for nested
  serialization, its constructor explicitly passes those fields to the Entity constructor as
  required by the generated mapping. This is model-boundary boilerplate, not a general constructor
  pattern.
- The `<feature>.actions.dart` exception in `ai/context/common.md` is one Flutter-specific grouping
  exception: action declarations and their closely related action value types are intentionally
  grouped there. The datasource interface/implementation pairing above and the `StatefulWidget`/
  `State<T>` pairing below are the other two.
- A `StatefulWidget` and its paired `State<T>` class may also share a file.
- A private widget class, or a method that returns widgets for a parent to render, still gets its
  own file with the appropriate suffix; being private is not a one-class-per-file exemption.
- Keep use cases to one public operation.
- Keep ViewModels as Redux-backed presentation connectors; business logic belongs in domain or
  state logic.
- Keep Flutter and DovahLink protocol types separate. Map protocol DTOs at the client boundary.
- Protocol DTOs must not cross into widgets or domain entities.
- Protocol wire DTOs, encoding/decoding, session validation, and transport-facing adapters belong
  in the feature's `data` boundary or an explicitly approved client-infrastructure area. Map
  structured external data to data Models before it enters domain code; pass Entities and ViewData
  to presentation code.

## Screens, sections, and widgets

- A Screen is the sole top-level content for its context.
- A Section is bespoke content nested inside a Screen alongside sibling content.
- A Widget is a reusable or repeatable unit with one cohesive purpose.
- Routability and the presence of a ViewModel do not decide the classification.
- A Screen, Section, Widget, or application presentation owner has one ViewModel only when it owns
  Redux-backed state or Redux actions. Each ViewModel belongs to exactly one owner and is named
  `<OwnerName>ViewModel`; child widgets do not receive ViewModels merely to transport UI values.
- A Redux-backed ViewModel is a named public class in its own `.viewmodel.dart` file. It exposes
  `factory OwnerNameViewModel.fromStore(Store<AppState> store)` and owns selector calls,
  Redux-backed presentation values, and action callbacks.
- Register every Redux-backed ViewModel in `sl` with `registerFactoryParam`, passing its
  `Store<AppState>` to `fromStore`.
- An owner that has a ViewModel uses this connector pattern:

  The connector converter only resolves the ViewModel through `sl`; it does not map state or create
  callbacks.
- An owner with a ViewModel does not call selectors, read `store.state`, derive Redux-backed
  values, call `store.dispatch`, create Redux callbacks, or perform Redux mapping in its converter.
- Widgets without Redux-backed state or actions receive values, Entities, ViewData, and callbacks
  through constructor parameters. ViewData is immutable presentation data; it does not receive a
  Store, use selectors, dispatch actions, or get registered in `sl`.

## Redux flow

- Use Redux when a value is read by another screen, drives a use case, or must persist beyond one
  widget rebuild. Purely local presentation state stays in the smallest widget's `State`.
- Keep shared presentation state in its owning widget's ViewModel and pass it to child widgets
  through props. Keep purely local presentation state in the smallest widget's `State`.
- Widgets use ViewModels for selector-derived presentation values and dispatch callbacks; middleware
  coordinates accepted operations, and reducers own state transitions.
- Widgets do not call selectors or `store.dispatch`, read `store.state`, or map Redux state.
- ViewModels read Redux state through selectors and create dispatch callbacks. State extraction and
  feature-level state decisions belong in selectors; ViewModels map selector results to their
  single owner's presentation contract.
- Consumers outside reducers read Redux state through selectors. Reducers operate directly on the
  state slice they own.
- When consumers share an interpretation or capability derived from state, one semantic selector
  owns that meaning. Presentation and middleware do not independently reproduce it.
- When state determines whether a user command is available, presentation consumes a semantic
  capability and does not offer the unavailable command. Keep that capability separate from status
  and data used to present the operation.
- Middleware may re-check the same semantic capability before starting asynchronous work. This
  defensive check prevents invalid or repeated dispatch from starting conflicting work; it does not
  transfer responsibility for normal command availability from presentation to middleware.
- The Redux store is created once during application composition. Store connectors suppress
  redundant rebuilds when their mapped presentation values remain equal.
- Reducers compose typed action-specific transitions; they do not inspect arbitrary actions in a
  broad conditional chain.
- Each feature owns its middleware and registers it through shared store composition.
- The public middleware entry point forwards actions through the chain and routes handled actions to
  private typed operation handlers. Keep operation policy, asynchronous orchestration, and result
  mapping in those handlers instead of growing the entry point into a central policy method.
- Private operation handlers receive only the store and the typed action they handle. They do not
  receive chain-forwarding callbacks or unrelated infrastructure arguments. This narrow framework
  boundary may resolve already-registered contracts as prescribed here; do not extend it to
  ordinary feature or infrastructure code.
- A request expresses intent. When asynchronous work must be admitted or validated first, the
  request alone does not claim that work has started; publish a separate lifecycle transition only
  after acceptance.
- When only one instance of an operation is valid at a time, use the authoritative application
  lifecycle or capability model to prevent competing work where possible. Do not duplicate that
  invariant with separate mutable in-progress state or concurrency machinery.
- Middleware maps each typed use-case outcome to the corresponding success or failure action.
- Share handler work through explicit typed actions rather than untyped raw-parameter helper calls.

## Feature call chain

Middleware calls the SDK contract directly for a complete SDK-owned operation. When the app owns a
domain decision or coordinates multiple sources, the operation follows the appropriate domain and
data boundaries.

Use a use case when the app owns a domain decision. Use a repository when the app coordinates
multiple sources, persistence, cache, or domain aggregation. Use a datasource when the Flutter app
owns external I/O. Do not add a UseCase, Repository, Datasource, Entity/Model pair, or other layer
whose only responsibility is forwarding an SDK operation unchanged. Use the SDK's registered
contract directly when it already owns the complete operation.

## Dependency injection

- Every behavior-bearing Flutter/Dart class or equivalent type has an explicit abstract contract,
  even when it has one implementation. Consumers depend on the contract. Constructor injection is
  the default for collaborators, including infrastructure implementations, protocol clients,
  repositories, datasources, use cases, services, and other behavior-bearing types. The narrow
  framework/composition-boundary exception is Redux middleware action handlers: as specified in
  "Redux flow", they may resolve only the already-registered use cases and services they invoke
  through `sl<Type>()`. Register concrete implementations behind their contracts in `GetIt`.
- Do not add artificial interfaces to widgets, DTOs, entities, enums, pure functions, or other
  data-only types. This rule is adopted phase-forward and does not reopen completed phases.
- Register shared dependencies first in `lib/injection_container.dart`, then call each feature's
  injection container.
- Infrastructure implementations, protocol clients, repositories, use cases, services, and ViewModels
  are registered in the manual `GetIt` container.
- Register concrete implementations behind domain interfaces.
- Register use cases as DI dependencies and construct them with repository interfaces.
- Register Redux-backed ViewModels with `registerFactoryParam`, receiving `Store<AppState>` and
  returning the ViewModel created by `fromStore`.
- Redux-backed ViewModels are presentation values, not behavior-bearing services; register and
  resolve their concrete classes as described above.
- Resolve a Redux-backed ViewModel through `sl` only in its owning `StoreConnector` converter.
- ViewModels, use cases, entities, repositories, datasources, and services never resolve
  collaborators from `GetIt` themselves.
- Call dependency initialization once before `runApp`.

## Services

- Do not create a generic feature service layer.
- App-wide plumbing without business rules belongs in `shared/utils/` and is DI-registered; it is
  not wrapped in a use case.
- Feature-specific orchestration stays in its owning feature; do not move it to `shared/utils/` to
  avoid choosing a feature boundary.
- One-off external I/O owned by the Flutter app belongs in the owning feature datasource. An
  SDK-owned operation with an adequate public contract may be called directly by its Redux
  middleware; do not wrap it in a feature service or forwarding-only architecture chain.

## Application shutdown

`AppShutdownService` is platform-neutral and owns one idempotent, three-second cleanup budget. It
starts `PairingMiddleware.shutdown()` first and then starts SDK disconnect before awaiting either
operation. Pairing shutdown immediately blocks new pairing work; SDK disconnect immediately
invalidates pending authentication and reconnect work. The pairing client registration records its
instance in the app lifecycle holder; shutdown must not resolve the lazy client registration just to
disconnect an unused client. Late authentication, code-request, or confirmation results cannot
dispatch follow-up pairing work after shutdown begins. The SDK disconnect is the final cleanup step,
so late completions start no further application work. Windows registers `WindowsLifecycleBridge`,
which forwards native close and session-ending requests to the shared service. Android and iOS do not
register that bridge, and
ordinary background/pause lifecycle events do not invoke application shutdown.

For a normal Windows close, the runner holds `WM_CLOSE` until Dart replies or a five-second native
timer expires; repeated close requests share that pending attempt, and only its current generation
can continue closing the window. After the handshake completes, the runner offers the close message
to Flutter's top-level window pipeline before falling back to native window destruction. Later close
messages for that window continue through Flutter without starting cleanup again, including the
engine's own close message. Dart returns when cleanup finishes or its three-second budget expires;
the deadline stops waiting but does not cancel cleanup already in progress. This leaves the native
timeout margin before the runner resumes close processing. `WM_QUERYENDSESSION`
returns success immediately without cleanup, since another application may cancel the system
request. When `WM_ENDSESSION` reports a committed session ending, the runner requests best-effort
Dart cleanup once and returns immediately; Windows may terminate the process before that cleanup
finishes. Neither path shuts down the separate Host.

## Theming and layout

- Colors and text styles come from `Theme.of(context)` and the approved app theme.
- Spacing and icon sizes come from shared layout constants or theme extensions, never inline widget
  literals once a token exists.
- Corner radii come from button themes or the shared shape theme extension.
- Widgets receive data and callbacks through props; visual styling comes from the theme, not from
  constructor parameters or hidden DI lookups.
- Visual values have one owner, chosen by what the value describes:
  - `DovahThemeTokens` owns theme identity and typography: semantic colors, status tones, corner
    style, radius and bevel, font families and their fallbacks, and casing. The prototype names its
    fonts as CSS stacks (`Inter` for the body; `"Arial Narrow"` and `Impact` for Frostbound and
    `Georgia` and `"Times New Roman"` for Dovah and Hearth as the display) but ships no font file,
    so the client copies the stack names, resolves them through the installed fonts and the
    platform default, and bundles no font. Bundling one needs a licensed font asset the maintainer
    supplies; never substitute a different typeface.
  - `DovahThemeMaterials` owns each theme's visual recipes: one layered material per component role
    (surface, raised, control, icon, primary action), the canvas atmosphere, the dialog backdrop,
    the connection card's decoration, and the appearance preview scene. A recipe is a small typed value of layers, never a set of
    per-texture scalar tokens, and every value is copied from the approved prototype with its
    selector cited. Widgets ask `DovahSurface` for a role and never inspect a recipe; a component
    whose shape its own metrics fix, such as an icon tile, overrides the corner treatment and keeps
    the theme's material.
  - Canvas atmosphere, component material texture, and feature-specific artwork are three
    different things and never mix. The atmosphere (`DovahEnvironmentBackground`) fills the world
    behind every component and belongs to the canvas. A material textures a component and never
    paints an environment image. Feature artwork (a character hero, a map, quest or inventory art)
    stays with its feature screen and never enters `DovahThemeMaterials`. Theme image paths are
    named constants shared by the atmosphere and the preview, so a preview draws the real theme.
  - `Dovah*Metrics` classes own structural and component geometry and responsive layout. A metrics
    class is named for one screen family or component family, never a single catch-all. Widgets
    consume resolved metrics; they never branch on the window size or the active preset themselves.
  - A local literal owns a genuinely one-off layout detail with no semantic reuse; do not promote
    it to a constant.
- Reproduce a prototype visual recipe with the closest Flutter primitive before accepting a
  difference. Where Flutter has no equivalent, document at the implementation what the prototype
  does, what Flutter does instead, and why the difference is acceptable.
- A prototype value that differs by theme or by breakpoint belongs in a metrics class, never a new
  theme token or a scale multiplier applied to a base value. Copy each value exactly from the
  approved prototype and cite its selector in the doc comment; do not merge nearby values into one
  shared constant.
- A metrics value that differs by theme lives in that family's `Dovah<Family>ThemeMetrics`
  `ThemeExtension`, holding the theme's exact value for every window mode and installed by each
  preset's theme builder, so Flutter's `ThemeData` transition interpolates it with `lerpDouble`,
  `EdgeInsets.lerp`, and the like instead of snapping it. `Dovah<Family>Metrics.forWindow` takes
  that extension and the window size and selects the window mode; a metrics class never resolves a
  theme-varying value from a preset identity, because a preset changes discretely mid-transition.
  A value that differs only by window stays in the resolver, and a family with no theme-varying
  value has no theme extension.

## JSON data Models and generated code

- Use `json_serializable` for data Models that map to or from JSON; do not hand-write repetitive
  `fromJson`/`toJson` mappings for protocol DTOs.
- Run generation with `dart run build_runner build` from the owning Flutter project.
- Generated `.g.dart` files belong beside the source data Model in the owning area and must never be hand-edited.
- `json_serializable` is a mapping tool, not the protocol contract. The canonical schema remains in `protocol/schema/`, and shared examples remain in `protocol/fixtures/`.
- Keep semantic validation outside generated code: protocol versions, revisions, message/payload pairing, session identity, security limits, and recovery rules must be validated by handwritten boundary code. A closed/finite wire value with a fixed enumerable vocabulary (an enum-shaped field) is the one exception: it may decode directly into a typed enum via `json_serializable`'s `@JsonValue` support, with an unrecognized value failing inside `fromJson` as a `ProtocolFormatException`, per `ai/context/sdk/api-design.md`'s "Protocol DTO decoding" -- this is a closed-set format check, not the business-rule semantic validation (versions, revisions, pairing, identity, limits, recovery) this rule protects.
- Configure generated Models to preserve required-versus-unavailable distinctions; a missing required field must not silently become `null`.

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
- Keep feature-level state extraction and decisions in selectors. A ViewModel may map selector
  results into its owning widget's presentation values; widgets do not derive Redux-backed values.
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

- The canonical visual source of truth is the final approved prototype, `DovahLink-Prototype-final`
  (`index.html`, `assets/themes.css`, `assets/branding.js`, and its image assets). Every "approved
  prototype" in this repository, in code comments and in these conventions, means that prototype;
  older prototype versions are stale and are never a reference. Cite the prototype's selector, not
  a file path, in a doc comment, and never hardcode a location of the prototype in production code.
  The prototype ships no font files, so the typography it names cannot be bundled from it.
- Check approved DovahLink design references before making a new visual decision. If no local reference or design system exists, record the decision and do not import an external design system without approval.
- Build the approved Skyrim-inspired presentation with native Flutter theming and components.
- Keep fonts, colors, panels, icons, spacing, and animations behind shared theme tokens or themed components so the Core UI Theme System can support future adapters.
- Do not hardcode colors, typography, spacing, icon sizes, or corner radii inside widgets once the theme system exists.
- Use the existing theme and layout tokens; add a new token before adding a repeated literal.
- Keep the native DovahLink theme complete and usable without installed-resource detection or a UI mod adapter; missing or unsupported adapter values must fall back to it.
