import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises connection selectors over root application state.
void main() {
  group('ConnectionSelectors', () {
    test('selects the phase and derived label from AppState', () {
      final AppState state = AppState(
        connection: const ConnectionState(
          phase: ConnectionPhase.negotiating,
          session: null,
          error: null,
        ),
        pairing: PairingState.initial(),
      );

      expect(
        ConnectionSelectors.phaseSelector(state),
        ConnectionPhase.negotiating,
      );
      expect(ConnectionSelectors.statusLabelSelector(state), 'Negotiating');
    });

    test('selects session and error values from AppState', () {
      final AppState state = AppState(
        connection: const ConnectionState(
          phase: ConnectionPhase.connected,
          session: ConnectionSessionEntity(sessionId: 'session-1'),
          error: 'Bridge unavailable',
        ),
        pairing: PairingState.initial(),
      );

      expect(ConnectionSelectors.sessionIdSelector(state), 'session-1');
      expect(ConnectionSelectors.errorSelector(state), 'Bridge unavailable');
    });

    test('selects the Bridge list from AppState', () {
      final BridgeEntity bridge = BridgeEntity(
        displayName: 'Local Bridge',
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final AppState state = AppState(
        connection: ConnectionState(
          phase: ConnectionPhase.disconnected,
          session: null,
          error: null,
          bridges: [bridge],
        ),
        pairing: PairingState.initial(),
      );

      expect(ConnectionSelectors.bridgesSelector(state), [bridge]);
    });
  });
}
