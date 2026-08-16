import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_reducer.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises root Redux reducer pass-through and delegation.
void main() {
  group('AppReducer processes unhandled actions correctly', () {
    test('Object modifies nothing', () {
      const AppState state = AppState(
        connection: ConnectionState(
          phase: ConnectionPhase.disconnected,
          session: null,
          error: null,
        ),
        pairing: PairingState(
          phase: PairingPhase.none,
          bridgeVersion: null,
          error: null,
        ),
      );

      expect(identical(appReducer(state, Object()), state), isTrue);
    });
  });

  group('AppReducer processes handled actions correctly', () {
    test('ConnectionStartedAction delegates to the connection reducer', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(
        state,
        const ConnectionStartedAction(),
      );

      expect(result, isA<AppState>());
      expect(result.connection.phase, ConnectionPhase.connecting);
      expect(result, isNot(same(state)));
    });

    test('PairingStartedAction delegates to the pairing reducer', () {
      final AppState state = AppState.initial();

      final AppState result = appReducer(state, const PairingStartedAction());

      expect(result, isA<AppState>());
      expect(result.pairing.phase, PairingPhase.connecting);
      expect(result, isNot(same(state)));
    });
  });
}
