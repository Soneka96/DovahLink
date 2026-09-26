import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises pairing-state initialization and copying.
void main() {
  group('PairingState — initial', () {
    test(
      'creates a state with no phase, host version, error, or timing values',
      () {
        final PairingState state = PairingState.initial();

        expect(state.phase, PairingPhase.none);
        expect(state.hostVersion, isNull);
        expect(state.error, isNull);
        expect(state.credentialRejectionReason, isNull);
        expect(state.codeExpiresAt, isNull);
        expect(state.renotifyAvailableAt, isNull);
        expect(state.isRenotifyPending, isFalse);
      },
    );
  });

  group('PairingState — direct constructor', () {
    test('creates a state with all fields including DateTime values', () {
      final DateTime expiresAt = DateTime.now();
      final DateTime availableAt = DateTime.now().add(
        const Duration(seconds: 5),
      );

      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        hostVersion: '1.2.3',
        error: null,
        credentialRejectionReason: PairingCredentialRejectionReason.blocked,
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: availableAt,
      );

      expect(state.phase, PairingPhase.awaitingCode);
      expect(state.hostVersion, '1.2.3');
      expect(state.error, isNull);
      expect(
        state.credentialRejectionReason,
        PairingCredentialRejectionReason.blocked,
      );
      expect(state.codeExpiresAt, expiresAt);
      expect(state.renotifyAvailableAt, availableAt);
    });
  });

  group('PairingState — copyWith', () {
    test('replaces the phase and preserves other values when omitted', () {
      final PairingState state = PairingState.initial();

      final PairingState result = state.copyWith(
        phase: PairingPhase.connecting,
      );

      expect(result.phase, PairingPhase.connecting);
      expect(result.hostVersion, isNull);
      expect(result.error, isNull);
      expect(result.credentialRejectionReason, isNull);
    });

    test('replaces and clears nullable values explicitly', () {
      const PairingState state = PairingState(
        phase: PairingPhase.trusted,
        hostVersion: '1.2.3',
        error: 'old error',
        codeExpiresAt: null,
        renotifyAvailableAt: null,
      );

      final PairingState result = state.copyWith(
        hostVersion: const None(),
        error: const None(),
      );

      expect(result.hostVersion, isNull);
      expect(result.error, isNull);
    });

    test('sets and updates code expiration time', () {
      final DateTime expiresAt = DateTime.now().add(
        const Duration(seconds: 30),
      );
      final PairingState state = PairingState.initial();

      final PairingState result = state.copyWith(
        codeExpiresAt: Some(expiresAt),
      );

      expect(result.codeExpiresAt, expiresAt);
      expect(result.renotifyAvailableAt, isNull);
    });

    test('clears code expiration time explicitly', () {
      final DateTime expiresAt = DateTime.now().add(
        const Duration(seconds: 30),
      );
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        hostVersion: '1.2.3',
        error: null,
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: null,
      );

      final PairingState result = state.copyWith(codeExpiresAt: const None());

      expect(result.codeExpiresAt, isNull);
    });

    test('sets and updates renotify availability time', () {
      final DateTime availableAt = DateTime.now().add(
        const Duration(seconds: 5),
      );
      final PairingState state = PairingState.initial();

      final PairingState result = state.copyWith(
        renotifyAvailableAt: Some(availableAt),
      );

      expect(result.renotifyAvailableAt, availableAt);
      expect(result.codeExpiresAt, isNull);
    });

    test('PairingState copyWith sets and clears redisplay pending state', () {
      final PairingState state = PairingState.initial().copyWith(
        isRenotifyPending: true,
      );

      expect(state.isRenotifyPending, isTrue);
      expect(
        state.copyWith(isRenotifyPending: false).isRenotifyPending,
        isFalse,
      );
    });

    test('clears renotify availability time explicitly', () {
      final DateTime availableAt = DateTime.now().add(
        const Duration(seconds: 5),
      );
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        hostVersion: '1.2.3',
        error: null,
        codeExpiresAt: null,
        renotifyAvailableAt: availableAt,
      );

      final PairingState result = state.copyWith(
        renotifyAvailableAt: const None(),
      );

      expect(result.renotifyAvailableAt, isNull);
    });

    test('updates both timing fields together', () {
      final DateTime expiresAt = DateTime.now().add(
        const Duration(seconds: 30),
      );
      final DateTime availableAt = DateTime.now().add(
        const Duration(seconds: 5),
      );
      final PairingState state = PairingState.initial();

      final PairingState result = state.copyWith(
        codeExpiresAt: Some(expiresAt),
        renotifyAvailableAt: Some(availableAt),
      );

      expect(result.codeExpiresAt, expiresAt);
      expect(result.renotifyAvailableAt, availableAt);
    });

    test('preserves timing fields when updating other fields', () {
      final DateTime expiresAt = DateTime.now().add(
        const Duration(seconds: 30),
      );
      final DateTime availableAt = DateTime.now().add(
        const Duration(seconds: 5),
      );
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        hostVersion: '1.2.3',
        error: null,
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: availableAt,
      );

      final PairingState result = state.copyWith(
        hostVersion: const Some('2.0.0'),
        error: const Some('connection lost'),
      );

      expect(result.hostVersion, '2.0.0');
      expect(result.error, 'connection lost');
      expect(result.codeExpiresAt, expiresAt);
      expect(result.renotifyAvailableAt, availableAt);
    });

    test('updates phase and clears error while preserving timing fields', () {
      final DateTime expiresAt = DateTime.now().add(
        const Duration(seconds: 30),
      );
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        hostVersion: '1.2.3',
        error: 'old error',
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: null,
      );

      final PairingState result = state.copyWith(
        phase: PairingPhase.confirming,
        error: const None(),
      );

      expect(result.phase, PairingPhase.confirming);
      expect(result.error, isNull);
      expect(result.codeExpiresAt, expiresAt);
    });
  });

  group('Behavior equality in PairingState behaves correctly', () {
    test(
      'Behavior equality in PairingState distinguishes pending redisplay',
      () {
        final PairingState idle = PairingState.initial();
        final PairingState pending = idle.copyWith(isRenotifyPending: true);

        expect(idle, isNot(pending));
      },
    );
  });

  group('Property credentialRejectionReason in PairingState behaves correctly', () {
    test(
      'Property credentialRejectionReason in PairingState stores and clears a typed reason',
      () {
        final PairingState state = PairingState.initial().copyWith(
          credentialRejectionReason: const Some(
            PairingCredentialRejectionReason.blocked,
          ),
        );

        expect(
          state.credentialRejectionReason,
          PairingCredentialRejectionReason.blocked,
        );

        final PairingState result = state.copyWith(
          credentialRejectionReason: const None(),
        );

        expect(result.credentialRejectionReason, isNull);
      },
    );

    test(
      'Property credentialRejectionReason in PairingState distinguishes rejection reasons',
      () {
        final PairingState revoked = PairingState.initial().copyWith(
          credentialRejectionReason: const Some(
            PairingCredentialRejectionReason.revoked,
          ),
        );
        final PairingState blocked = PairingState.initial().copyWith(
          credentialRejectionReason: const Some(
            PairingCredentialRejectionReason.blocked,
          ),
        );

        expect(revoked == blocked, isFalse);
      },
    );
  });
}
