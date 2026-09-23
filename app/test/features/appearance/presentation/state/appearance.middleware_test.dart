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
      () async {
        when(
          () => mockSetThemePreset(any()),
        ).thenAnswer((_) async => const Right(unit));

        middleware.call(
          store,
          const ThemePresetSelectedAction(DovahThemePreset.hearth),
          next,
        );
        await Future<void>.delayed(Duration.zero);

        verify(
          () => mockSetThemePreset(
            const SetThemePresetParams(preset: DovahThemePreset.hearth),
          ),
        ).called(1);
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
      () async {
        const DatabaseFailure failure = DatabaseFailure('unavailable');
        when(
          () => mockSetThemePreset(any()),
        ).thenAnswer((_) async => const Left(failure));

        middleware.call(
          store,
          const ThemePresetSelectedAction(DovahThemePreset.hearth),
          next,
        );
        await Future<void>.delayed(Duration.zero);

        // Only the triggering action, logged by `next`; the failure is a background concern and
        // dispatches nothing further, per the handler's own documented reasoning.
        expect(actionLog, [
          const ThemePresetSelectedAction(DovahThemePreset.hearth),
        ]);
      },
    );
  });

  group('AppearanceMiddleware processes unhandled actions correctly', () {
    test('An unhandled action calls next and resolves no use case', () {
      middleware.call(store, Object(), next);

      expect(actionLog, [isA<Object>()]);
      verifyZeroInteractions(mockSetThemePreset);
    });
  });
}
