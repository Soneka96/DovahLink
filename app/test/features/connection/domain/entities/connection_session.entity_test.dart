import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';

/// Exercises connection-session entity value preservation.
void main() {
  group('ConnectionSessionEntity', () {
    test('stores the server-issued session identity and protocol version', () {
      const ConnectionSessionEntity session = ConnectionSessionEntity(
        sessionId: 'session-1',
        protocolVersion: 1,
      );

      expect(session.sessionId, 'session-1');
      expect(session.protocolVersion, 1);
      expect(session.bridgeInstanceId, isNull);
      expect(session.playContextId, isNull);
    });

    test('stores bridge and play-context identity for a v2 session', () {
      const ConnectionSessionEntity session = ConnectionSessionEntity(
        sessionId: 'session-1',
        protocolVersion: 2,
        bridgeInstanceId: 'bridge-1',
        playContextId: 'context-1',
      );

      expect(session.bridgeInstanceId, 'bridge-1');
      expect(session.playContextId, 'context-1');
    });

    test('treats sessions with different bridge identity as unequal', () {
      const ConnectionSessionEntity first = ConnectionSessionEntity(
        sessionId: 'session-1',
        protocolVersion: 2,
        bridgeInstanceId: 'bridge-1',
      );
      const ConnectionSessionEntity second = ConnectionSessionEntity(
        sessionId: 'session-1',
        protocolVersion: 2,
        bridgeInstanceId: 'bridge-2',
      );

      expect(first == second, isFalse);
    });
  });
}
