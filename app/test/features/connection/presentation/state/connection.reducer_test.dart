import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.reducer.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises connection lifecycle reducer transitions.
void main() {
  const ConnectionSessionEntity session = ConnectionSessionEntity(
    sessionId: 'session-1',
    protocolVersion: 1,
  );

  group('ConnectionReducer processes lifecycle actions correctly', () {
    test('ConnectionStartedAction changes the phase to connecting', () {
      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        const ConnectionStartedAction(),
      );

      expect(result.phase, ConnectionPhase.connecting);
      expect(result.session, isNull);
      expect(result.error, isNull);
    });

    test('ConnectionStartedAction clears a previous session and error', () {
      const ConnectionState state = ConnectionState(
        phase: ConnectionPhase.unavailable,
        session: session,
        error: 'old error',
      );

      final ConnectionState result = connectionReducer(
        state,
        const ConnectionStartedAction(),
      );

      expect(result.session, isNull);
      expect(result.error, isNull);
    });

    test('ConnectionNegotiatingAction changes the phase to negotiating', () {
      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        const ConnectionNegotiatingAction(),
      );

      expect(result.phase, ConnectionPhase.negotiating);
      expect(result.error, isNull);
    });

    test('ConnectionEstablishedAction stores the accepted session', () {
      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        const ConnectionEstablishedAction(session),
      );

      expect(result.phase, ConnectionPhase.connected);
      expect(result.session, session);
      expect(result.error, isNull);
    });

    test('ConnectionEstablishedAction replaces the previous session', () {
      const ConnectionSessionEntity previousSession = ConnectionSessionEntity(
        sessionId: 'session-0',
        protocolVersion: 1,
      );
      const ConnectionState state = ConnectionState(
        phase: ConnectionPhase.recovering,
        session: previousSession,
        error: null,
      );

      final ConnectionState result = connectionReducer(
        state,
        const ConnectionEstablishedAction(session),
      );

      expect(result.session, session);
      expect(result.session, isNot(previousSession));
    });

    test('ConnectionRecoveringAction preserves the active session', () {
      const ConnectionState state = ConnectionState(
        phase: ConnectionPhase.connected,
        session: session,
        error: 'old error',
      );

      final ConnectionState result = connectionReducer(
        state,
        const ConnectionRecoveringAction(),
      );

      expect(result.session, session);
      expect(result.error, isNull);
    });

    test('ConnectionRecoveringAction changes the phase to recovering', () {
      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        const ConnectionRecoveringAction(),
      );

      expect(result.phase, ConnectionPhase.recovering);
      expect(result.error, isNull);
    });

    test('ConnectionUnavailableAction stores the message', () {
      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        const ConnectionUnavailableAction('Bridge is unavailable'),
      );

      expect(result.phase, ConnectionPhase.unavailable);
      expect(result.error, 'Bridge is unavailable');
    });

    test('ConnectionIncompatibleAction stores the message', () {
      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        const ConnectionIncompatibleAction('Protocol mismatch'),
      );

      expect(result.phase, ConnectionPhase.incompatible);
      expect(result.error, 'Protocol mismatch');
    });

    test('ConnectionDisconnectedAction clears session and error', () {
      final ConnectionState result = connectionReducer(
        const ConnectionState(
          phase: ConnectionPhase.connected,
          session: session,
          error: 'old error',
        ),
        const ConnectionDisconnectedAction(),
      );

      expect(result.phase, ConnectionPhase.disconnected);
      expect(result.session, isNull);
      expect(result.error, isNull);
    });
  });

  group('ConnectionReducer processes unhandled actions correctly', () {
    test('Object modifies nothing', () {
      final ConnectionState state = ConnectionState.initial();

      expect(identical(connectionReducer(state, Object()), state), isTrue);
    });
  });
}
