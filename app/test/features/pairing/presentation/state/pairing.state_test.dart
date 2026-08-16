import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises pairing-state initialization and copying.
void main() {
  group('PairingState — initial', () {
    test('creates a state with no phase, bridge version, or error', () {
      final PairingState state = PairingState.initial();

      expect(state.phase, PairingPhase.none);
      expect(state.bridgeVersion, isNull);
      expect(state.error, isNull);
    });
  });

  group('PairingState — copyWith', () {
    test('replaces the phase and preserves other values when omitted', () {
      final PairingState state = PairingState.initial();

      final PairingState result = state.copyWith(
        phase: PairingPhase.connecting,
      );

      expect(result.phase, PairingPhase.connecting);
      expect(result.bridgeVersion, isNull);
      expect(result.error, isNull);
    });

    test('replaces and clears nullable values explicitly', () {
      const PairingState state = PairingState(
        phase: PairingPhase.trusted,
        bridgeVersion: '1.2.3',
        error: 'old error',
      );

      final PairingState result = state.copyWith(
        bridgeVersion: const None(),
        error: const None(),
      );

      expect(result.bridgeVersion, isNull);
      expect(result.error, isNull);
    });
  });
}
