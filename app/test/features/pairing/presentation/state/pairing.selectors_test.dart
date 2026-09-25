import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Builds pairing state with the supplied phase, copy, and rejection reason.
AppState buildPairingState({
  PairingPhase phase = PairingPhase.none,
  String? error,
  PairingCredentialRejectionReason? rejectionReason,
}) => AppState(
  connection: ConnectionState.initial(),
  pairing: PairingState(
    phase: phase,
    hostVersion: null,
    error: error,
    credentialRejectionReason: rejectionReason,
    codeExpiresAt: null,
    renotifyAvailableAt: null,
  ),
);

/// Exercises pairing selectors over root application state.
void main() {
  group('PairingSelectors', () {
    test('selects the phase and derived label from AppState', () {
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: const PairingState(
          phase: PairingPhase.awaitingCode,
          hostVersion: null,
          error: null,
          codeExpiresAt: null,
          renotifyAvailableAt: null,
        ),
      );

      expect(PairingSelectors.phaseSelector(state), PairingPhase.awaitingCode);
      expect(PairingSelectors.statusLabelSelector(state), 'Awaiting code');
    });

    test('selects host version and error values from AppState', () {
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: const PairingState(
          phase: PairingPhase.failed,
          hostVersion: '1.2.3',
          error: 'Host unavailable',
          codeExpiresAt: null,
          renotifyAvailableAt: null,
        ),
      );

      expect(PairingSelectors.hostVersionSelector(state), '1.2.3');
      expect(PairingSelectors.errorSelector(state), 'Host unavailable');
    });
  });

  group('PairingSelectors.codeCountdownSecondsSelector', () {
    test('returns null when codeExpiresAt is null', () {
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: const PairingState(
          phase: PairingPhase.awaitingCode,
          hostVersion: null,
          error: null,
          codeExpiresAt: null,
          renotifyAvailableAt: null,
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
          hostVersion: null,
          error: null,
          codeExpiresAt: expiresIn10Seconds,
          renotifyAvailableAt: null,
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
          hostVersion: null,
          error: null,
          codeExpiresAt: expiredInPast,
          renotifyAvailableAt: null,
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
          hostVersion: null,
          error: null,
          codeExpiresAt: null,
          renotifyAvailableAt: null,
        ),
      );

      expect(PairingSelectors.renotifyCooldownSecondsSelector(state), isNull);
    });

    test('returns positive seconds when cooldown is active', () {
      final now = DateTime.now();
      final availableIn5Seconds = now.add(const Duration(seconds: 5));
      final AppState state = AppState(
        connection: ConnectionState.initial(),
        pairing: PairingState(
          phase: PairingPhase.awaitingCode,
          hostVersion: null,
          error: null,
          codeExpiresAt: null,
          renotifyAvailableAt: availableIn5Seconds,
        ),
      );

      final remaining = PairingSelectors.renotifyCooldownSecondsSelector(state);
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
          hostVersion: null,
          error: null,
          codeExpiresAt: null,
          renotifyAvailableAt: elapsedInPast,
        ),
      );

      expect(PairingSelectors.renotifyCooldownSecondsSelector(state), 0);
    });
  });
  group('Selector canDismissSelector behaves correctly', () {
    test('Selector canDismissSelector returns false only while confirming', () {
      for (final PairingPhase phase in PairingPhase.values) {
        final AppState state = AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: phase,
            hostVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: null,
          ),
        );

        expect(
          PairingSelectors.canDismissSelector(state),
          phase != PairingPhase.confirming,
          reason: '$phase',
        );
      }
    });
  });

  group('Selector isRepairSelector behaves correctly', () {
    test(
      'Selector isRepairSelector returns true for revoked and unrecognized credentials',
      () {
        for (final PairingCredentialRejectionReason reason in [
          PairingCredentialRejectionReason.revoked,
          PairingCredentialRejectionReason.unrecognized,
        ]) {
          expect(
            PairingSelectors.isRepairSelector(
              buildPairingState(
                phase: PairingPhase.unpaired,
                rejectionReason: reason,
              ),
            ),
            isTrue,
            reason: '$reason',
          );
        }
      },
    );

    test(
      'Selector isRepairSelector returns false for blocked credentials and no reason',
      () {
        expect(
          PairingSelectors.isRepairSelector(
            buildPairingState(
              phase: PairingPhase.unpaired,
              rejectionReason: PairingCredentialRejectionReason.blocked,
            ),
          ),
          isFalse,
        );
        expect(
          PairingSelectors.isRepairSelector(
            buildPairingState(phase: PairingPhase.unpaired),
          ),
          isFalse,
        );
      },
    );

    test('Selector isRepairSelector ignores messages and other phases', () {
      expect(
        PairingSelectors.isRepairSelector(
          buildPairingState(
            phase: PairingPhase.unpaired,
            error: "This device's trust was revoked.",
          ),
        ),
        isFalse,
      );
      for (final PairingPhase phase in PairingPhase.values) {
        if (phase == PairingPhase.unpaired) {
          continue;
        }
        expect(
          PairingSelectors.isRepairSelector(
            buildPairingState(
              phase: phase,
              rejectionReason: PairingCredentialRejectionReason.revoked,
            ),
          ),
          isFalse,
          reason: '$phase',
        );
      }
    });
  });

  group('Selector isBlockedSelector behaves correctly', () {
    test(
      'Selector isBlockedSelector returns true only for an unpaired blocked credential',
      () {
        expect(
          PairingSelectors.isBlockedSelector(
            buildPairingState(
              phase: PairingPhase.unpaired,
              rejectionReason: PairingCredentialRejectionReason.blocked,
            ),
          ),
          isTrue,
        );
        for (final PairingCredentialRejectionReason reason in [
          PairingCredentialRejectionReason.revoked,
          PairingCredentialRejectionReason.unrecognized,
        ]) {
          expect(
            PairingSelectors.isBlockedSelector(
              buildPairingState(
                phase: PairingPhase.unpaired,
                rejectionReason: reason,
              ),
            ),
            isFalse,
            reason: '$reason',
          );
        }
        expect(
          PairingSelectors.isBlockedSelector(
            buildPairingState(phase: PairingPhase.unpaired),
          ),
          isFalse,
        );
        expect(
          PairingSelectors.isBlockedSelector(
            buildPairingState(
              phase: PairingPhase.failed,
              rejectionReason: PairingCredentialRejectionReason.blocked,
            ),
          ),
          isFalse,
        );
      },
    );
  });

  group('Selector credentialRejectionReasonSelector behaves correctly', () {
    test(
      'Selector credentialRejectionReasonSelector returns the typed reason or null',
      () {
        expect(
          PairingSelectors.credentialRejectionReasonSelector(
            buildPairingState(
              rejectionReason: PairingCredentialRejectionReason.blocked,
            ),
          ),
          PairingCredentialRejectionReason.blocked,
        );
        expect(
          PairingSelectors.credentialRejectionReasonSelector(
            buildPairingState(),
          ),
          isNull,
        );
      },
    );
  });

  group('Selector renotifyPendingSelector behaves correctly', () {
    test(
      'Selector renotifyPendingSelector returns true while redisplay is pending',
      () {
        final AppState state = AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState.initial().copyWith(isRenotifyPending: true),
        );

        expect(PairingSelectors.renotifyPendingSelector(state), isTrue);
      },
    );

    test('Selector renotifyPendingSelector returns false when idle', () {
      expect(
        PairingSelectors.renotifyPendingSelector(AppState.initial()),
        isFalse,
      );
    });
  });
}
