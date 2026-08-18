import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/app/composition_root.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises independent store creation by [AppCompositionRoot].
void main() {
  group('AppCompositionRoot', () {
    test('creates an independent store with initial state', () {
      const AppCompositionRoot root = AppCompositionRoot();

      final firstStore = root.createStore();
      final secondStore = root.createStore();

      expect(firstStore.state, isA<AppState>());
      expect(firstStore.state.pairing, isA<PairingState>());
      expect(secondStore.state, isA<AppState>());
      expect(identical(firstStore, secondStore), isFalse);
    });
  });
}
