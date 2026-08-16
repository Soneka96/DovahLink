import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises pairing selectors over root application state.
void main() {
  group('PairingSelectors', () {
    test('selects the phase and derived label from AppState', () {
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: const PairingState(
          phase: PairingPhase.awaitingCode,
          bridgeVersion: null,
          error: null,
        ),
      );

      expect(
        PairingSelectors.phaseSelector(state),
        PairingPhase.awaitingCode,
      );
      expect(PairingSelectors.statusLabelSelector(state), 'Awaiting code');
    });

    test('selects bridge version and error values from AppState', () {
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: const PairingState(
          phase: PairingPhase.failed,
          bridgeVersion: '1.2.3',
          error: 'Bridge unavailable',
        ),
      );

      expect(PairingSelectors.bridgeVersionSelector(state), '1.2.3');
      expect(PairingSelectors.errorSelector(state), 'Bridge unavailable');
    });
  });
}
