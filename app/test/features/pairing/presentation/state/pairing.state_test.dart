import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises pairing-state initialization and copying.
void main() {
  group('PairingState — initial', () {
    test('creates a state with no phase, bridge version, error, or timing values', () {
      final PairingState state = PairingState.initial();

      expect(state.phase, PairingPhase.none);
      expect(state.bridgeVersion, isNull);
      expect(state.error, isNull);
      expect(state.codeExpiresAt, isNull);
      expect(state.renotifyAvailableAt, isNull);
    });
  });

  group('PairingState — direct constructor', () {
    test('creates a state with all fields including DateTime values', () {
      final DateTime expiresAt = DateTime.now();
      final DateTime availableAt = DateTime.now().add(const Duration(seconds: 5));

      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
        error: null,
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: availableAt,
      );

      expect(state.phase, PairingPhase.awaitingCode);
      expect(state.bridgeVersion, '1.2.3');
      expect(state.error, isNull);
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
      expect(result.bridgeVersion, isNull);
      expect(result.error, isNull);
    });

    test('replaces and clears nullable values explicitly', () {
      const PairingState state = PairingState(
        phase: PairingPhase.trusted,
        bridgeVersion: '1.2.3',
        error: 'old error',
        codeExpiresAt: null,
        renotifyAvailableAt: null,
      );

      final PairingState result = state.copyWith(
        bridgeVersion: const None(),
        error: const None(),
      );

      expect(result.bridgeVersion, isNull);
      expect(result.error, isNull);
    });

    test('sets and updates code expiration time', () {
      final DateTime expiresAt = DateTime.now().add(const Duration(seconds: 30));
      final PairingState state = PairingState.initial();

      final PairingState result = state.copyWith(
        codeExpiresAt: Some(expiresAt),
      );

      expect(result.codeExpiresAt, expiresAt);
      expect(result.renotifyAvailableAt, isNull);
    });

    test('clears code expiration time explicitly', () {
      final DateTime expiresAt = DateTime.now().add(const Duration(seconds: 30));
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
        error: null,
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: null,
      );

      final PairingState result = state.copyWith(
        codeExpiresAt: const None(),
      );

      expect(result.codeExpiresAt, isNull);
    });

    test('sets and updates renotify availability time', () {
      final DateTime availableAt = DateTime.now().add(const Duration(seconds: 5));
      final PairingState state = PairingState.initial();

      final PairingState result = state.copyWith(
        renotifyAvailableAt: Some(availableAt),
      );

      expect(result.renotifyAvailableAt, availableAt);
      expect(result.codeExpiresAt, isNull);
    });

    test('clears renotify availability time explicitly', () {
      final DateTime availableAt = DateTime.now().add(const Duration(seconds: 5));
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
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
      final DateTime expiresAt = DateTime.now().add(const Duration(seconds: 30));
      final DateTime availableAt = DateTime.now().add(const Duration(seconds: 5));
      final PairingState state = PairingState.initial();

      final PairingState result = state.copyWith(
        codeExpiresAt: Some(expiresAt),
        renotifyAvailableAt: Some(availableAt),
      );

      expect(result.codeExpiresAt, expiresAt);
      expect(result.renotifyAvailableAt, availableAt);
    });

    test('preserves timing fields when updating other fields', () {
      final DateTime expiresAt = DateTime.now().add(const Duration(seconds: 30));
      final DateTime availableAt = DateTime.now().add(const Duration(seconds: 5));
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
        error: null,
        codeExpiresAt: expiresAt,
        renotifyAvailableAt: availableAt,
      );

      final PairingState result = state.copyWith(
        bridgeVersion: const Some('2.0.0'),
        error: const Some('connection lost'),
      );

      expect(result.bridgeVersion, '2.0.0');
      expect(result.error, 'connection lost');
      expect(result.codeExpiresAt, expiresAt);
      expect(result.renotifyAvailableAt, availableAt);
    });

    test('updates phase and clears error while preserving timing fields', () {
      final DateTime expiresAt = DateTime.now().add(const Duration(seconds: 30));
      final PairingState state = PairingState(
        phase: PairingPhase.awaitingCode,
        bridgeVersion: '1.2.3',
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
}
