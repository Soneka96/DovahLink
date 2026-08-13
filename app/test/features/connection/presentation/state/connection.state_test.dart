import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises connection-state initialization and copying.
void main() {
  group('ConnectionState — initial', () {
    test('creates a disconnected state with no session or error', () {
      final ConnectionState state = ConnectionState.initial();

      expect(state.phase, ConnectionPhase.disconnected);
      expect(state.session, isNull);
      expect(state.error, isNull);
    });
  });

  group('ConnectionState — copyWith', () {
    test('replaces the phase and preserves other values when omitted', () {
      final ConnectionState state = ConnectionState.initial();

      final ConnectionState result = state.copyWith(
        phase: ConnectionPhase.connecting,
      );

      expect(result.phase, ConnectionPhase.connecting);
      expect(result.session, isNull);
      expect(result.error, isNull);
    });

    test('replaces and clears nullable values explicitly', () {
      const ConnectionSessionEntity session = ConnectionSessionEntity(
        sessionId: 'session-1',
        protocolVersion: 1,
      );
      const ConnectionState state = ConnectionState(
        phase: ConnectionPhase.connected,
        session: session,
        error: 'old error',
      );

      final ConnectionState result = state.copyWith(
        session: const None(),
        error: const None(),
      );

      expect(result.session, isNull);
      expect(result.error, isNull);
    });
  });
}
