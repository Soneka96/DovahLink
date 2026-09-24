import 'package:flutter/services.dart' show PlatformException;

import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/app/composition_root.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Mocks async preference reads for composition-root tests.
class MockSharedPreferencesAsync extends Mock
    implements SharedPreferencesAsync {}

/// Exercises independent store creation by [AppCompositionRoot] through the real dependency
/// graph [initDependencies] wires -- a composition test proving the production graph resolves
/// correctly, not a second suite for any one collaborator's own behavior.
void main() {
  late MockSharedPreferencesAsync preferences;

  setUp(() async {
    TestWidgetsFlutterBinding.ensureInitialized();
    await sl.reset();
    await initDependencies();
    preferences = MockSharedPreferencesAsync();
    when(
      () => preferences.getString('dovahlink.appearance.themePreset'),
    ).thenAnswer((_) async => null);
    await sl.unregister<SharedPreferencesAsync>();
    sl.registerSingleton<SharedPreferencesAsync>(preferences);
  });

  tearDown(() async {
    await sl.reset();
  });

  group('Method createStore behaves correctly', () {
    test(
      'Method createStore creates independent stores with initial state',
      () async {
        const AppCompositionRoot root = AppCompositionRoot();

        final firstStore = await root.createStore();
        final secondStore = await root.createStore();

        expect(firstStore.state, isA<AppState>());
        expect(firstStore.state.pairing, isA<PairingState>());
        expect(secondStore.state, isA<AppState>());
        expect(identical(firstStore, secondStore), isFalse);
      },
    );

    test(
      'Method createStore hydrates appearance state from a persisted preset',
      () async {
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenAnswer((_) async => 'hearth');
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();

        expect(store.state.appearance.activePreset, DovahThemePreset.hearth);
      },
    );

    test(
      'Method createStore uses the default appearance when no preset is persisted',
      () async {
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();

        expect(store.state.appearance.activePreset, defaultThemePreset);
      },
    );

    test(
      'Method createStore uses the default appearance when preset loading fails',
      () async {
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenAnswer(
          (_) => Future<String?>.error(PlatformException(code: 'read-failed')),
        );
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();

        expect(store.state.appearance.activePreset, defaultThemePreset);
        verify(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).called(1);
      },
    );
  });
}
