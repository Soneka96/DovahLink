# Flutter testing

## Test priorities

- Test every intentional behavior branch in domain logic and reducers. Do not claim completion from a percentage alone; list intentionally untested defensive or platform branches and explain why they are excluded.
- Widgets and sections should have behavior-focused tests rather than snapshots.
- Screens should test the important user-visible states, failure states, and accessibility behavior.
- Protocol mapping tests belong at the client boundary and should use representative wire fixtures.
- Tests for JSON models must consume representative protocol fixtures and exercise every generated
  mapping direction the model exposes. Models intended to round-trip assert both `fromJson`
  decoding and `toJson` output. Generated source itself is not hand-tested or edited; handwritten
  boundary validation is tested explicitly.
- Model tests must also assert that each model is usable as its corresponding domain entity; entity behavior tests belong beside the entity when the entity contains behavior beyond value declarations.
- For each behavior exposed by a client boundary, tests must cover the applicable accepted and
  rejected messages, recovery requests, correlation IDs, session-generation invalidation, stale
  publication suppression, malformed or unsupported messages, duplicate and stale messages,
  revision gaps, snapshot recovery, and late messages after disposal.

## Test structure

- Mirror the source tree under `test/`.
- Add a dedicated test file for each source unit with behavior or failure logic; keep trivial
  declaration assertions in that source unit's test file without creating a class-wide test group.
- Keep ordinary client fixtures below the feature-owned
  `app/test/features/<feature>/fixtures/` directory, with subfolders mirroring the production
  ownership below `lib/features/<feature>/`. For example, an entity fixture belongs below
  `app/test/features/pairing/fixtures/domain/entities/` in repository-relative form. Use
  descriptive `.fixture.dart` files, share builders only within that feature, and apply the shared
  Dart fixture-builder rules. Keep one cohesive fixture family per file rather than creating one
  giant fixture or constants file. Cross-side protocol fixtures are an explicit exception and live
  in `protocol/fixtures/`; those canonical fixtures take precedence for contract tests.
- Mock the interface the code depends on; do not mock a concrete implementation when an interface exists.
- Assert exact arguments for delegated calls.
- Test both the action/call path and the matching no-action/no-call path when behavior is conditional.
- Define `setUp` per group, or in `main` when setup is shared by the whole file.
- Use literal Dart syntax for equality conditions, including enum values and null checks.
- Primitive properties use two assertions in order: `isA<T>()`, then the exact expected value.

## Layer-specific tests

- Models group each `fromJson`, `toJson`, or other mapping method separately. Keep an identity
  assertion in its own behavior group only when model/entity identity is itself the contract.
- Use cases group their single callable operation by method, normally `Method call behaves
  correctly`. Mock the repository interface and test success and every relevant failure pass-through.
- Repositories have one behavior group per method, including exact datasource calls and symmetric
  no-call branches.
- Reducers have one group per action passed to the reducer, including a separate group for the
  unhandled-action pass-through behavior, named `Action <ActionType> behaves correctly` (for
  example `Action PairingStartedAction behaves correctly`), mirroring `ai/context/dart/dart-style.md`'s
  `Method`/`Property`/`Behavior` template with `Action` as the label for this Flutter/Redux-specific
  case. Per that same doc's "Test organization", each test description repeats the action as its
  subject: `<ActionType> <verb...>`, for example `'PairingStartedAction changes the phase to
  connecting'`, never a bare `'changes the phase to connecting'` that only reads as a sentence once
  paired with its group.
- Middleware has one group per action passed to `middleware.call(store, action, next)`, rather than
  one group for the middleware class, named `<MiddlewareClass> processes <ActionType> correctly`
  (for example `PairingMiddleware processes PairingStartedAction correctly`) -- the middleware class
  stays in the group name for at-a-glance readability even though the action, not the class, is the
  actual grouping key. Per `ai/context/dart/dart-style.md`'s "Test organization", each test
  description repeats the action as its subject: `<ActionType> <verb...>`, for example
  `'PairingStartedAction dispatches PairingAuthenticatedAction when authentication succeeds'`, never
  a bare `'dispatches PairingAuthenticatedAction when authentication succeeds'` that only reads as a
  sentence once paired with its group. Middleware tests call
  `middleware.call(store, action, next)` directly rather than dispatching through a real `Store` --
  against a mocktail `MockStore` (`store.state` stubbed per test, no
  live reducer behind it), the same "mock every `sl<>()`-resolved dependency, no real store"
  approach widget tests use. `next` and `store.dispatch` both append to one shared action log
  (`void next(dynamic action) => actionLog.add(action);`,
  `when(() => store.dispatch(any())).thenAnswer((i) => actionLog.add(i.positionalArguments[0]));`),
  so the log holds the triggering action first (recorded by `next`, since the handled action is
  invoked directly rather than dispatched into a chain) followed by whatever the middleware itself
  dispatches as a result -- those result actions are only ever logged, never actually reduced.
  Assert against the log's contents and order, never a reducer-derived `store.state`.
- Datasource tests cover every distinct catch, guard, explicit throw, and malformed-input branch.

## Test naming

- Use `contains` for structural widget presence, `displays` for visible text, and `calls` or
  `dispatches` for side effects.
- Use “in state” for values inside `AppState`; reserve “in the store” for the `Store` instance.
- Add “with correct parameters” when the test verifies a configured state rather than bare
  presence.
- Per `ai/context/dart/dart-style.md`'s "Test organization", a widget/screen test description
  repeats its subject -- the widget or screen class name a `contains`/`displays`/`calls`/
  `dispatches` group already names -- rather than reading as a bare fragment that only makes sense
  paired with its group. Wrong: `group('PairingScreen contains widgets', () { testWidgets('contains
  the request-code button when unpaired', ...) })`. Right: `testWidgets('PairingScreen contains the
  request-code button when unpaired', ...)` -- identical grouping, but the test description repeats
  `PairingScreen` as its subject.

## Widget tests

- Test behavior through stable keys or semantics, not incidental widget types.
- Stub every dependency the widget reads during build, with no exception for infrastructure that
  feels like "just wiring." A screen resolves its ViewModel through `sl<ViewModel>(param1: store)`
  like any other DI-resolved dependency, so mock it the same way, unconditionally, in every test
  for that screen: a mocktail `class MockFooViewModel extends Mock implements FooViewModel {}`.
  Never a hand-built literal ViewModel instance, and never a real store driven through the reducer
  just to reach the phase/state a test wants -- reducer correctness and ViewModel derivation
  already have their own test files; a screen test's job is only "given this ViewModel, does the
  screen render/call the right thing." The `Store<AppState>` passed to `StoreProvider` is mocked
  too (`class MockStore extends Mock implements Store<AppState> {}`) -- the same
  `MockStore`/action-log pattern this project's middleware tests already use
  (`when(() => store.dispatch(any())).thenAnswer((i) => actionLog.add(i.positionalArguments[0]))`),
  plus `when(() => store.onChange).thenAnswer((_) => const Stream<AppState>.empty());` so
  `StoreConnector`'s internal subscription has a stream to listen to. A screen's
  `onInit`/`onDispose` hook, which reads/dispatches on the raw `Store` rather than through the
  ViewModel, is proven this way: stub `store.state` directly to whatever `AppState` the assertion
  needs and assert against the recorded action log -- never construct a real `Store`/reducer pair
  and dispatch real actions into it just to make a state true. `store.state` is a static
  `thenReturn`, not a live reducer, so it never reflects what `store.dispatch` recorded; a test
  that needs to assert state *after* a dispatch restubs `store.state` for that specific case
  rather than expecting the mock to update itself.
- Build the mocks once in `setUp` (ViewModel and Store both), stubbing every getter the screen
  touches during build to a baseline default; individual tests override only the specific
  `when()` stubs their assertion needs, rather than constructing fresh mocks per test. Reset the
  DI container and every mock in `tearDown` (`await sl.reset(); reset(mockViewModel); reset(store);`).
- Wrap the screen in a single reusable `buildWidget({...})` helper (theme/textScaler as
  parameters) instead of repeating `MaterialApp`/`StoreProvider` boilerplate in every test.
- Widget tests group each structural, interaction, callback, accessibility, or StoreConnector
  behavior separately. Selector tests use one group per selector, not one group for a selectors
  class or file.
- For every state exposed by the client-state contract, test the corresponding presentation behavior. If a screen intentionally does not render a state, test that it delegates to the approved fallback and document the decision. Cover loading, success, empty, error, disconnected, stale-data, recovering, and disposed-subscription behavior where exposed.

## Timers and delays

- Code that schedules a `Timer` or `Future.delayed` is tested with `package:fake_async`'s
  `fakeAsync`/`FakeAsync.elapse` to control time deterministically, not real waits.
- `testWidgets` tests do not need `fakeAsync`: `TestWidgetsFlutterBinding` already fakes the clock
  for `pump`/`pumpAndSettle`.

## Accessibility

Accessibility checks must use concrete assertions or approved tooling: semantic labels, minimum tap-target dimensions, text-scaling layout behavior, focus traversal order, and contrast against approved theme colors. A screen change is incomplete if an applicable check is absent or skipped without maintainer approval. Keep these checks in the screen's own test file rather than a global accessibility file.

## What not to test

- Generated code
- Framework behavior
- Pure wiring with no decision or failure path
- Snapshot output
- Multi-hop navigation sequences (push/pop/go chains) in a route-configuration test; assert only
  that each route resolves to its screen
