import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_reducer.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises root Redux reducer pass-through and delegation.
void main() {
  group('AppReducer processes unhandled actions correctly', () {
    test('Object modifies nothing', () {
      final AppState state = AppState.initial();

      expect(identical(appReducer(state, Object()), state), isTrue);
    });
  });

  group('AppReducer processes handled actions correctly', () {
    test('PairingStartedAction delegates to the pairing reducer', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(state, const PairingStartedAction());

      expect(result, isA<AppState>());
      expect(result.pairing.phase, PairingPhase.connecting);
      expect(result, isNot(same(state)));
    });

    test('PairingStartedAction leaves AppState.connection unchanged', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(state, const PairingStartedAction());

      expect(identical(result.connection, state.connection), isTrue);
    });
  });
}
