import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/app/composition_root.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises independent store creation by [AppCompositionRoot] through the real dependency
/// graph [initDependencies] wires -- a composition test proving the production graph resolves
/// correctly, not a second suite for any one collaborator's own behavior.
void main() {
  tearDown(() async {
    await sl.reset();
  });

  group('AppCompositionRoot', () {
    test('creates an independent store with initial state', () async {
      TestWidgetsFlutterBinding.ensureInitialized();
      SharedPreferences.setMockInitialValues(<String, Object>{});
      await initDependencies();
      const AppCompositionRoot root = AppCompositionRoot();

      final firstStore = await root.createStore();
      final secondStore = await root.createStore();

      expect(firstStore.state, isA<AppState>());
      expect(firstStore.state.pairing, isA<PairingState>());
      expect(secondStore.state, isA<AppState>());
      expect(identical(firstStore, secondStore), isFalse);
    });

    test(
      'hydrates the store\'s initial appearance state from a persisted preset',
      () async {
        TestWidgetsFlutterBinding.ensureInitialized();
        SharedPreferences.setMockInitialValues(<String, Object>{
          'dovahlink.appearance.themePreset': 'hearth',
        });
        await initDependencies();
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();

        expect(store.state.appearance.activePreset, DovahThemePreset.hearth);
      },
    );

    test(
      'hydrates the store\'s initial appearance state to the default when nothing is persisted',
      () async {
        TestWidgetsFlutterBinding.ensureInitialized();
        SharedPreferences.setMockInitialValues(<String, Object>{});
        await initDependencies();
        const AppCompositionRoot root = AppCompositionRoot();

        final store = await root.createStore();

        expect(store.state.appearance.activePreset, defaultThemePreset);
      },
    );
  });
}
