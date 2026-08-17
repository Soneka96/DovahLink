import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.reducer.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises pairing lifecycle reducer transitions.
void main() {
  group('PairingReducer processes lifecycle actions correctly', () {
    test('PairingStartedAction changes the phase to connecting', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingStartedAction(),
      );

      expect(result.phase, PairingPhase.connecting);
      expect(result.error, isNull);
    });

    test('PairingStartedAction clears a previous error', () {
      const PairingState state = PairingState(
        phase: PairingPhase.failed,
        bridgeVersion: null,
        error: 'old error',
      );

      final PairingState result = pairingReducer(
        state,
        const PairingStartedAction(),
      );

      expect(result.error, isNull);
    });

    test(
      'PairingAuthenticatedAction stores the bridge version and moves to trusted when already trusted',
      () {
        final PairingState result = pairingReducer(
          PairingState.initial(),
          const PairingAuthenticatedAction(
            bridgeVersion: '1.2.3',
            trusted: true,
          ),
        );

        expect(result.phase, PairingPhase.trusted);
        expect(result.bridgeVersion, '1.2.3');
        expect(result.error, isNull);
      },
    );

    test(
      'PairingAuthenticatedAction stores the bridge version and moves to unpaired when not trusted',
      () {
        final PairingState result = pairingReducer(
          PairingState.initial(),
          const PairingAuthenticatedAction(
            bridgeVersion: '1.2.3',
            trusted: false,
          ),
        );

        expect(result.phase, PairingPhase.unpaired);
        expect(result.bridgeVersion, '1.2.3');
      },
    );

    test('PairingCodeRequestedAction changes the phase to requestingCode', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingCodeRequestedAction(),
      );

      expect(result.phase, PairingPhase.requestingCode);
      expect(result.error, isNull);
    });

    test('PairingCodeAvailableAction changes the phase to awaitingCode', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingCodeAvailableAction(),
      );

      expect(result.phase, PairingPhase.awaitingCode);
      expect(result.error, isNull);
    });

    test('PairingCodeSubmittedAction changes the phase to confirming', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingCodeSubmittedAction(code: '123456'),
      );

      expect(result.phase, PairingPhase.confirming);
      expect(result.error, isNull);
    });

    test('PairingConfirmedAction changes the phase to trusted', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingConfirmedAction(),
      );

      expect(result.phase, PairingPhase.trusted);
      expect(result.error, isNull);
    });

    test(
      'PairingDisconnectedAction changes the phase to disconnected, clears '
      'error, and preserves bridgeVersion',
      () {
        const PairingState state = PairingState(
          phase: PairingPhase.connecting,
          bridgeVersion: '1.2.3',
          error: 'old error',
        );

        final PairingState result = pairingReducer(
          state,
          const PairingDisconnectedAction(),
        );

        expect(result.phase, PairingPhase.disconnected);
        expect(result.error, isNull);
        expect(result.bridgeVersion, '1.2.3');
      },
    );

    test('PairingFailedAction stores the message and phase', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingFailedAction('That code isn\'t correct.'),
      );

      expect(result.phase, PairingPhase.failed);
      expect(result.error, "That code isn't correct.");
    });

    test('PairingDisposedAction resets phase, bridge version, and error', () {
      const PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
        error: 'old error',
      );

      final PairingState result = pairingReducer(
        state,
        const PairingDisposedAction(wasTrusted: false),
      );

      expect(result.phase, PairingPhase.none);
      expect(result.bridgeVersion, isNull);
      expect(result.error, isNull);
    });

    test(
      'PairingDisposedAction resets phase, bridge version, and error regardless of wasTrusted',
      () {
        const PairingState state = PairingState(
          phase: PairingPhase.trusted,
          bridgeVersion: '1.2.3',
          error: null,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingDisposedAction(wasTrusted: true),
        );

        expect(result.phase, PairingPhase.none);
        expect(result.bridgeVersion, isNull);
        expect(result.error, isNull);
      },
    );
  });

  group('PairingReducer processes unhandled actions correctly', () {
    test('Object modifies nothing', () {
      final PairingState state = PairingState.initial();

      expect(identical(pairingReducer(state, Object()), state), isTrue);
    });
  });
}
