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

      expect(PairingSelectors.phaseSelector(state), PairingPhase.awaitingCode);
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

  group('PairingSelectors.codeCountdownSecondsSelector', () {
    test('returns null when codeExpiresAt is null', () {
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: const PairingState(
          phase: PairingPhase.awaitingCode,
          codeExpiresAt: null,
        ),
      );

      expect(PairingSelectors.codeCountdownSecondsSelector(state), isNull);
    });

    test('returns positive seconds when code expires in the future', () {
      final now = DateTime.now();
      final expiresIn10Seconds = now.add(const Duration(seconds: 10));
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: PairingState(
          phase: PairingPhase.awaitingCode,
          codeExpiresAt: expiresIn10Seconds,
        ),
      );

      final remaining = PairingSelectors.codeCountdownSecondsSelector(state);
      expect(remaining, isNotNull);
      expect(remaining, lessThanOrEqualTo(10));
      expect(remaining, greaterThanOrEqualTo(9));
    });

    test('returns 0 when code has already expired', () {
      final now = DateTime.now();
      final expiredInPast = now.subtract(const Duration(seconds: 5));
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: PairingState(
          phase: PairingPhase.awaitingCode,
          codeExpiresAt: expiredInPast,
        ),
      );

      expect(PairingSelectors.codeCountdownSecondsSelector(state), 0);
    });
  });

  group('PairingSelectors.renotifyCooldownSecondsSelector', () {
    test('returns null when renotifyAvailableAt is null', () {
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: const PairingState(
          phase: PairingPhase.awaitingCode,
          renotifyAvailableAt: null,
        ),
      );

      expect(
        PairingSelectors.renotifyCooldownSecondsSelector(state),
        isNull,
      );
    });

    test('returns positive seconds when cooldown is active', () {
      final now = DateTime.now();
      final availableIn5Seconds = now.add(const Duration(seconds: 5));
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: PairingState(
          phase: PairingPhase.awaitingCode,
          renotifyAvailableAt: availableIn5Seconds,
        ),
      );

      final remaining =
          PairingSelectors.renotifyCooldownSecondsSelector(state);
      expect(remaining, isNotNull);
      expect(remaining, lessThanOrEqualTo(5));
      expect(remaining, greaterThanOrEqualTo(4));
    });

    test('returns 0 when cooldown has elapsed', () {
      final now = DateTime.now();
      final elapsedInPast = now.subtract(const Duration(seconds: 3));
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: PairingState(
          phase: PairingPhase.awaitingCode,
          renotifyAvailableAt: elapsedInPast,
        ),
      );

      expect(PairingSelectors.renotifyCooldownSecondsSelector(state), 0);
    });
  });
}
