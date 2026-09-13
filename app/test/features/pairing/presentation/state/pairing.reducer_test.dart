import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.reducer.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Exercises pairing lifecycle reducer transitions.
void main() {
  group('Action PairingStartedAction behaves correctly', () {
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
        codeExpiresAt: null,
        renotifyAvailableAt: null,
      );

      final PairingState result = pairingReducer(
        state,
        const PairingStartedAction(),
      );

      expect(result.error, isNull);
    });
  });

  group('Action PairingAuthenticatedAction behaves correctly', () {
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
        expect(result.error, isNull);
      },
    );

    test(
      'PairingAuthenticatedAction carries the credential-rejected message through as the error',
      () {
        final PairingState result = pairingReducer(
          PairingState.initial(),
          const PairingAuthenticatedAction(
            bridgeVersion: '1.2.3',
            trusted: false,
            credentialRejectedMessage: "This device's trust was revoked.",
          ),
        );

        expect(result.phase, PairingPhase.unpaired);
        expect(result.error, "This device's trust was revoked.");
      },
    );

    test(
      'PairingAuthenticatedAction clears a pre-existing error when no credential was rejected',
      () {
        const PairingState state = PairingState(
          phase: PairingPhase.connecting,
          bridgeVersion: null,
          error: 'old error',
          codeExpiresAt: null,
          renotifyAvailableAt: null,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingAuthenticatedAction(
            bridgeVersion: '1.2.3',
            trusted: true,
          ),
        );

        expect(result.error, isNull);
      },
    );
  });

  group('Action PairingCodeRequestedAction behaves correctly', () {
    test('PairingCodeRequestedAction changes the phase to requestingCode', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingCodeRequestedAction(),
      );

      expect(result.phase, PairingPhase.requestingCode);
      expect(result.error, isNull);
    });
  });

  group('Action PairingCodeAvailableAction behaves correctly', () {
    test('PairingCodeAvailableAction changes the phase to awaitingCode', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingCodeAvailableAction(),
      );

      expect(result.phase, PairingPhase.awaitingCode);
      expect(result.error, isNull);
      expect(result.codeExpiresAt, isNull);
    });

    test(
      'PairingCodeAvailableAction sets codeExpiresAt when expiresInSeconds is present',
      () {
        final PairingState result = pairingReducer(
          PairingState.initial(),
          const PairingCodeAvailableAction(expiresInSeconds: 30),
        );

        expect(result.phase, PairingPhase.awaitingCode);
        expect(result.codeExpiresAt, isNotNull);
        expect(result.codeExpiresAt!.isAfter(DateTime.now()), isTrue);
      },
    );

    test(
      'PairingCodeAvailableAction sets codeExpiresAt when expiresInSeconds is zero',
      () {
        final PairingState result = pairingReducer(
          PairingState.initial(),
          const PairingCodeAvailableAction(expiresInSeconds: 0),
        );

        expect(result.codeExpiresAt, isNotNull);
      },
    );

    test(
      'PairingCodeAvailableAction clears a stale codeExpiresAt when expiresInSeconds is null',
      () {
        final DateTime staleExpiresAt = DateTime.now();
        final PairingState state = PairingState(
          phase: PairingPhase.requestingCode,
          bridgeVersion: '1.2.3',
          error: null,
          codeExpiresAt: staleExpiresAt,
          renotifyAvailableAt: null,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingCodeAvailableAction(),
        );

        expect(result.codeExpiresAt, isNull);
      },
    );

    test(
      'PairingCodeAvailableAction clears a stale renotifyAvailableAt from a previous challenge',
      () {
        final DateTime staleRenotifyAvailableAt = DateTime.now().add(
          const Duration(seconds: 5),
        );
        final PairingState state = PairingState(
          phase: PairingPhase.requestingCode,
          bridgeVersion: '1.2.3',
          error: null,
          codeExpiresAt: null,
          renotifyAvailableAt: staleRenotifyAvailableAt,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingCodeAvailableAction(expiresInSeconds: 30),
        );

        expect(result.renotifyAvailableAt, isNull);
      },
    );
  });

  group('Action PairingCodeSubmittedAction behaves correctly', () {
    test('PairingCodeSubmittedAction changes the phase to confirming', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingCodeSubmittedAction(code: '123456'),
      );

      expect(result.phase, PairingPhase.confirming);
      expect(result.error, isNull);
    });
  });

  group('Action PairingConfirmedAction behaves correctly', () {
    test('PairingConfirmedAction changes the phase to trusted', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingConfirmedAction(),
      );

      expect(result.phase, PairingPhase.trusted);
      expect(result.error, isNull);
    });
  });

  group('Action PairingDisconnectedAction behaves correctly', () {
    test('PairingDisconnectedAction changes the phase to disconnected, clears '
        'error, and preserves bridgeVersion', () {
      const PairingState state = PairingState(
        phase: PairingPhase.connecting,
        bridgeVersion: '1.2.3',
        error: 'old error',
        codeExpiresAt: null,
        renotifyAvailableAt: null,
      );

      final PairingState result = pairingReducer(
        state,
        const PairingDisconnectedAction(),
      );

      expect(result.phase, PairingPhase.disconnected);
      expect(result.error, isNull);
      expect(result.bridgeVersion, '1.2.3');
    });
  });

  group('Action PairingFailedAction behaves correctly', () {
    test('PairingFailedAction stores the message and phase', () {
      final PairingState result = pairingReducer(
        PairingState.initial(),
        const PairingFailedAction('That code isn\'t correct.'),
      );

      expect(result.phase, PairingPhase.failed);
      expect(result.error, "That code isn't correct.");
    });

    test('PairingFailedAction presents an administrative session invalidation the same as any '
        'other failure', () {
      // The reducer has no reason-specific branch: an administrative invalidation's real
      // SessionInvalidatedFailure message reaches PairingPhase.failed exactly like any other
      // PairingFailedAction, which is what makes all four administrative reasons present
      // identically -- there is no code path here that could distinguish them.
      const SessionInvalidatedFailure failure = SessionInvalidatedFailure(
        'This device was disconnected by the bridge. Try again.',
      );

      final PairingState result = pairingReducer(
        PairingState.initial(),
        PairingFailedAction(failure.message),
      );

      expect(result.phase, PairingPhase.failed);
      expect(result.error, failure.message);
    });

    test(
      'PairingFailedAction clears codeExpiresAt and renotifyAvailableAt from the leftover '
      'challenge',
      () {
        final PairingState state = PairingState(
          phase: PairingPhase.awaitingCode,
          bridgeVersion: '1.2.3',
          error: null,
          codeExpiresAt: DateTime.now(),
          renotifyAvailableAt: DateTime.now().add(const Duration(seconds: 5)),
        );

        final PairingState result = pairingReducer(
          state,
          const PairingFailedAction('Too many wrong attempts.'),
        );

        expect(result.phase, PairingPhase.failed);
        expect(result.codeExpiresAt, isNull);
        expect(result.renotifyAvailableAt, isNull);
      },
    );
  });

  group('Action PairingDisposedAction behaves correctly', () {
    test('PairingDisposedAction resets phase, bridge version, and error', () {
      const PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
        error: 'old error',
        codeExpiresAt: null,
        renotifyAvailableAt: null,
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
          codeExpiresAt: null,
          renotifyAvailableAt: null,
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

  group('Action PairingRenotifyRequestedAction behaves correctly', () {
    test(
      'PairingRenotifyRequestedAction clears error, stays in awaitingCode',
      () {
        const PairingState state = PairingState(
          phase: PairingPhase.awaitingCode,
          bridgeVersion: '1.2.3',
          error: 'old error',
          codeExpiresAt: null,
          renotifyAvailableAt: null,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingRenotifyRequestedAction(),
        );

        expect(result.phase, PairingPhase.awaitingCode);
        expect(result.error, isNull);
        expect(result.bridgeVersion, '1.2.3');
        expect(result.codeExpiresAt, isNull);
        expect(result.renotifyAvailableAt, isNull);
      },
    );

    test('PairingRenotifyRequestedAction preserves timing fields', () {
      final DateTime expiresAt = DateTime.now();
      final DateTime availableAt = DateTime.now().add(
        const Duration(seconds: 5),
      );
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
        error: 'old error',
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: availableAt,
      );

      final PairingState result = pairingReducer(
        state,
        const PairingRenotifyRequestedAction(),
      );

      expect(result.codeExpiresAt, expiresAt);
      expect(result.renotifyAvailableAt, availableAt);
    });
  });

  group('Action PairingRenotifySucceededAction behaves correctly', () {
    test(
      'PairingRenotifySucceededAction clears error, stays in awaitingCode',
      () {
        const PairingState state = PairingState(
          phase: PairingPhase.awaitingCode,
          bridgeVersion: '1.2.3',
          error: 'old error',
          codeExpiresAt: null,
          renotifyAvailableAt: null,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingRenotifySucceededAction(),
        );

        expect(result.phase, PairingPhase.awaitingCode);
        expect(result.error, isNull);
        expect(result.bridgeVersion, '1.2.3');
      },
    );

    test('PairingRenotifySucceededAction preserves timing fields', () {
      final DateTime expiresAt = DateTime.now();
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
        error: 'old error',
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: null,
      );

      final PairingState result = pairingReducer(
        state,
        const PairingRenotifySucceededAction(),
      );

      expect(result.codeExpiresAt, expiresAt);
    });
  });

  group('Action PairingRenotifyCooldownAction behaves correctly', () {
    test('PairingRenotifyCooldownAction sets renotifyAvailableAt', () {
      const PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
        error: null,
        codeExpiresAt: null,
        renotifyAvailableAt: null,
      );

      final PairingState result = pairingReducer(
        state,
        const PairingRenotifyCooldownAction(retryAfterSeconds: 5),
      );

      expect(result.phase, PairingPhase.awaitingCode);
      expect(result.renotifyAvailableAt, isNotNull);
      expect(result.renotifyAvailableAt!.isAfter(DateTime.now()), isTrue);
    });

    test('PairingRenotifyCooldownAction preserves codeExpiresAt and error', () {
      final DateTime expiresAt = DateTime.now();
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
        error: 'wrong code',
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: null,
      );

      final PairingState result = pairingReducer(
        state,
        const PairingRenotifyCooldownAction(retryAfterSeconds: 3),
      );

      expect(result.codeExpiresAt, expiresAt);
      expect(result.error, 'wrong code');
    });
  });

  group('Action PairingCancelSucceededAction behaves correctly', () {
    test(
      'PairingCancelSucceededAction clears timing and transitions to failed',
      () {
        final DateTime expiresAt = DateTime.now();
        final PairingState state = PairingState(
          phase: PairingPhase.awaitingCode,
          bridgeVersion: '1.2.3',
          error: null,
          codeExpiresAt: expiresAt,
          renotifyAvailableAt: null,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingCancelSucceededAction(),
        );

        expect(result.phase, PairingPhase.failed);
        expect(result.error, 'Pairing cancelled.');
        expect(result.codeExpiresAt, isNull);
        expect(result.renotifyAvailableAt, isNull);
        expect(result.bridgeVersion, '1.2.3');
      },
    );
  });

  group(
    'Action PairingConfirmFailedWithAttemptsRemainingAction behaves correctly',
    () {
      test(
        'PairingConfirmFailedWithAttemptsRemainingAction keeps awaitingCode with error',
        () {
          const PairingState state = PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: '1.2.3',
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: null,
          );

          final PairingState result = pairingReducer(
            state,
            const PairingConfirmFailedWithAttemptsRemainingAction(
              message: "That code isn't correct.",
            ),
          );

          expect(result.phase, PairingPhase.awaitingCode);
          expect(result.error, "That code isn't correct.");
          expect(result.bridgeVersion, '1.2.3');
        },
      );

      test(
        'PairingConfirmFailedWithAttemptsRemainingAction preserves timing fields',
        () {
          final DateTime expiresAt = DateTime.now();
          final DateTime availableAt = DateTime.now().add(
            const Duration(seconds: 5),
          );
          final PairingState state = PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: '1.2.3',
            error: null,
            codeExpiresAt: expiresAt,
            renotifyAvailableAt: availableAt,
          );

          final PairingState result = pairingReducer(
            state,
            const PairingConfirmFailedWithAttemptsRemainingAction(
              message: 'invalid',
            ),
          );

          expect(result.codeExpiresAt, expiresAt);
          expect(result.renotifyAvailableAt, availableAt);
        },
      );

      test(
        'PairingConfirmFailedWithAttemptsRemainingAction returns to awaitingCode from its real '
        'predecessor, confirming',
        () {
          const PairingState state = PairingState(
            phase: PairingPhase.confirming,
            bridgeVersion: '1.2.3',
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: null,
          );

          final PairingState result = pairingReducer(
            state,
            const PairingConfirmFailedWithAttemptsRemainingAction(
              message: "That code isn't correct.",
            ),
          );

          expect(result.phase, PairingPhase.awaitingCode);
          expect(result.error, "That code isn't correct.");
        },
      );
    },
  );

  group('Action PairingConnectionRestoredAction behaves correctly', () {
    test(
      'PairingConnectionRestoredAction changes the phase to trusted, clears error, and '
      'preserves bridgeVersion',
      () {
        const PairingState state = PairingState(
          phase: PairingPhase.disconnected,
          bridgeVersion: '1.2.3',
          error: null,
          codeExpiresAt: null,
          renotifyAvailableAt: null,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingConnectionRestoredAction(),
        );

        expect(result.phase, PairingPhase.trusted);
        expect(result.error, isNull);
        expect(result.bridgeVersion, '1.2.3');
      },
    );

    test(
      'PairingConnectionRestoredAction clears a leftover error from the lost-connection phase',
      () {
        const PairingState state = PairingState(
          phase: PairingPhase.disconnected,
          bridgeVersion: '1.2.3',
          error: 'old error',
          codeExpiresAt: null,
          renotifyAvailableAt: null,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingConnectionRestoredAction(),
        );

        expect(result.error, isNull);
      },
    );

    test(
      'PairingConnectionRestoredAction stays trusted when a redundant event arrives while '
      'already trusted (e.g. the observation stream replaying its current value on subscribe)',
      () {
        const PairingState state = PairingState(
          phase: PairingPhase.trusted,
          bridgeVersion: '1.2.3',
          error: null,
          codeExpiresAt: null,
          renotifyAvailableAt: null,
        );

        final PairingState result = pairingReducer(
          state,
          const PairingConnectionRestoredAction(),
        );

        expect(result.phase, PairingPhase.trusted);
        expect(result.error, isNull);
        expect(result.bridgeVersion, '1.2.3');
      },
    );
  });

  group('Behavior unhandled-action pass-through behaves correctly', () {
    test(
      'Behavior unhandled-action pass-through leaves state unchanged for an action with no '
      'registered reducer',
      () {
        final PairingState state = PairingState.initial();

        expect(identical(pairingReducer(state, Object()), state), isTrue);
      },
    );
  });
}
