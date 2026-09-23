import 'dart:async';

import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/domain/usecases/params/set_theme_preset.params.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/set_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Mocks the use case [AppearanceMiddleware] resolves through [sl].
class MockSetThemePresetUseCase extends Mock implements SetThemePresetUseCase {}

/// Mocktail double for [Store], called directly rather than dispatched through -- `next` and the
/// mocked `store.dispatch` both log into [actionLog], so any action the middleware itself
/// dispatches is observed there, never reduced.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [AppearanceMiddleware] in isolation: each test calls `middleware.call(store, action,
/// next)` directly with the action under test, rather than dispatching through a real Store.
void main() {
  late AppearanceMiddleware middleware;
  late MockSetThemePresetUseCase mockSetThemePreset;
  late MockStore store;
  late List<Object?> actionLog;

  /// Records the action passed to the middleware's next handler.
  void next(dynamic action) => actionLog.add(action);

  setUpAll(() {
    registerFallbackValue(
      const SetThemePresetParams(preset: DovahThemePreset.dovah),
    );
  });

  setUp(() async {
    await sl.reset();
    middleware = AppearanceMiddleware();
    mockSetThemePreset = MockSetThemePresetUseCase();
    sl.registerLazySingleton<SetThemePresetUseCase>(() => mockSetThemePreset);

    actionLog = [];
    store = MockStore();
    when(() => store.dispatch(any())).thenAnswer(
      (Invocation invocation) =>
          actionLog.add(invocation.positionalArguments[0]),
    );
  });

  tearDown(() async {
    await sl.reset();
    reset(mockSetThemePreset);
    reset(store);
  });

  group('AppearanceMiddleware processes ThemePresetSelectedAction correctly', () {
    test(
      'ThemePresetSelectedAction persists the selected preset through SetThemePresetUseCase',
      () {
        when(
          () => mockSetThemePreset(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(
          store,
          const ThemePresetSelectedAction(DovahThemePreset.hearth),
          next,
        );

        verify(
          () => mockSetThemePreset(
            const SetThemePresetParams(preset: DovahThemePreset.hearth),
          ),
        ).called(1);
      },
    );

    test(
      'ThemePresetSelectedAction persists rapid selections in selection order',
      () {
        fakeAsync((FakeAsync async) {
          final Completer<Either<Failure, Unit>> hearthPersistence =
              Completer<Either<Failure, Unit>>();
          final Completer<Either<Failure, Unit>> dovahPersistence =
              Completer<Either<Failure, Unit>>();
          final List<DovahThemePreset> persistenceStarts = [];
          when(() => mockSetThemePreset(any())).thenAnswer((Invocation call) {
            final SetThemePresetParams params =
                call.positionalArguments.single as SetThemePresetParams;
            persistenceStarts.add(params.preset);
            return params.preset == DovahThemePreset.hearth
                ? hearthPersistence.future
                : dovahPersistence.future;
          });

          middleware.call(
            store,
            const ThemePresetSelectedAction(DovahThemePreset.hearth),
            next,
          );
          middleware.call(
            store,
            const ThemePresetSelectedAction(DovahThemePreset.dovah),
            next,
          );

          expect(actionLog, [
            const ThemePresetSelectedAction(DovahThemePreset.hearth),
            const ThemePresetSelectedAction(DovahThemePreset.dovah),
          ]);

          async.flushMicrotasks();
          expect(persistenceStarts, [DovahThemePreset.hearth]);

          hearthPersistence.complete(const Right(unit));
          async.flushMicrotasks();
          expect(persistenceStarts, [
            DovahThemePreset.hearth,
            DovahThemePreset.dovah,
          ]);

          dovahPersistence.complete(const Right(unit));
          async.flushMicrotasks();
        });
      },
    );

    test(
      'ThemePresetSelectedAction persists the next preset after a write fails',
      () {
        const DatabaseFailure failure = DatabaseFailure('unavailable');
        fakeAsync((FakeAsync async) {
          final Completer<Either<Failure, Unit>> hearthPersistence =
              Completer<Either<Failure, Unit>>();
          final List<DovahThemePreset> persistenceStarts = [];
          when(() => mockSetThemePreset(any())).thenAnswer((Invocation call) {
            final SetThemePresetParams params =
                call.positionalArguments.single as SetThemePresetParams;
            persistenceStarts.add(params.preset);
            return params.preset == DovahThemePreset.hearth
                ? hearthPersistence.future
                : Future<Either<Failure, Unit>>.value(const Right(unit));
          });

          middleware.call(
            store,
            const ThemePresetSelectedAction(DovahThemePreset.hearth),
            next,
          );
          middleware.call(
            store,
            const ThemePresetSelectedAction(DovahThemePreset.dovah),
            next,
          );

          async.flushMicrotasks();
          expect(persistenceStarts, [DovahThemePreset.hearth]);

          hearthPersistence.complete(const Left(failure));
          async.flushMicrotasks();

          expect(persistenceStarts, [
            DovahThemePreset.hearth,
            DovahThemePreset.dovah,
          ]);
        });
      },
    );

    test(
      'ThemePresetSelectedAction persists the next preset after a write throws',
      () {
        fakeAsync((FakeAsync async) {
          final Completer<Either<Failure, Unit>> hearthPersistence =
              Completer<Either<Failure, Unit>>();
          final List<DovahThemePreset> persistenceStarts = [];
          when(() => mockSetThemePreset(any())).thenAnswer((Invocation call) {
            final SetThemePresetParams params =
                call.positionalArguments.single as SetThemePresetParams;
            persistenceStarts.add(params.preset);
            return params.preset == DovahThemePreset.hearth
                ? hearthPersistence.future
                : Future<Either<Failure, Unit>>.value(const Right(unit));
          });

          middleware.call(
            store,
            const ThemePresetSelectedAction(DovahThemePreset.hearth),
            next,
          );
          middleware.call(
            store,
            const ThemePresetSelectedAction(DovahThemePreset.dovah),
            next,
          );

          async.flushMicrotasks();
          expect(persistenceStarts, [DovahThemePreset.hearth]);

          hearthPersistence.completeError(StateError('unavailable'));
          async.flushMicrotasks();

          expect(persistenceStarts, [
            DovahThemePreset.hearth,
            DovahThemePreset.dovah,
          ]);
        });
      },
    );

    test(
      'ThemePresetSelectedAction calls next exactly once with the triggering action',
      () {
        when(
          () => mockSetThemePreset(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(
          store,
          const ThemePresetSelectedAction(DovahThemePreset.hearth),
          next,
        );

        expect(actionLog, [
          const ThemePresetSelectedAction(DovahThemePreset.hearth),
        ]);
      },
    );

    test(
      'ThemePresetSelectedAction does not dispatch when persistence fails',
      () {
        const DatabaseFailure failure = DatabaseFailure('unavailable');
        fakeAsync((FakeAsync async) {
          final Completer<Either<Failure, Unit>> persistence =
              Completer<Either<Failure, Unit>>();
          when(
            () => mockSetThemePreset(any()),
          ).thenAnswer((_) => persistence.future);

          middleware.call(
            store,
            const ThemePresetSelectedAction(DovahThemePreset.hearth),
            next,
          );
          persistence.complete(const Left(failure));
          async.flushMicrotasks();

          // Only the triggering action, logged by `next`; the failure does not add an action.
          expect(actionLog, [
            const ThemePresetSelectedAction(DovahThemePreset.hearth),
          ]);
        });
      },
    );
  });

  group('AppearanceMiddleware processes Object correctly', () {
    test('Object calls next and resolves no use case', () {
      middleware.call(store, Object(), next);

      expect(actionLog, [isA<Object>()]);
      verifyZeroInteractions(mockSetThemePreset);
    });
  });
}
