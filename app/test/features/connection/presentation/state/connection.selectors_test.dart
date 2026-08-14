import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises connection selectors over root application state.
void main() {
  group('ConnectionSelectors', () {
    test('selects the phase and derived label from AppState', () {
      const AppState state = AppState(
        connection: ConnectionState(
          phase: ConnectionPhase.negotiating,
          session: null,
          error: null,
        ),
      );

      expect(
        ConnectionSelectors.phaseSelector(state),
        ConnectionPhase.negotiating,
      );
      expect(ConnectionSelectors.statusLabelSelector(state), 'Negotiating');
    });

    test('selects session and error values from AppState', () {
      const AppState state = AppState(
        connection: ConnectionState(
          phase: ConnectionPhase.connected,
          session: ConnectionSessionEntity(
            sessionId: 'session-1',
            protocolVersion: 1,
          ),
          error: 'Bridge unavailable',
        ),
      );

      expect(ConnectionSelectors.sessionIdSelector(state), 'session-1');
      expect(ConnectionSelectors.errorSelector(state), 'Bridge unavailable');
    });
  });
}
