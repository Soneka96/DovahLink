import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connection_status_screen.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_reducer.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Exercises [ConnectionStatusScreenViewModel.fromStore] projections.
void main() {
  group('ConnectionStatusScreenViewModel fromStore()', () {
    test(
      'fromStore constructs a disconnected ViewModel correctly',
      () {
        final ConnectionStatusScreenViewModel viewModel =
            ConnectionStatusScreenViewModel.fromStore(const CreateStore()());

        expect(viewModel.phase, ConnectionPhase.disconnected);
        expect(viewModel.statusLabel, 'Disconnected');
        expect(viewModel.sessionId, isNull);
        expect(viewModel.error, isNull);
      },
    );

    test('fromStore constructs a connected ViewModel correctly', () {
      final Store<AppState> store = const CreateStore()();
      store.dispatch(
        const ConnectionEstablishedAction(
          ConnectionSessionEntity(sessionId: 'session-1'),
        ),
      );

      final ConnectionStatusScreenViewModel viewModel =
          ConnectionStatusScreenViewModel.fromStore(store);

      expect(viewModel.phase, ConnectionPhase.connected);
      expect(viewModel.statusLabel, 'Connected');
      expect(viewModel.sessionId, 'session-1');
      expect(viewModel.error, isNull);
    });

    test('fromStore labels every lifecycle phase correctly', () {
      final List<MapEntry<ConnectionPhase, String>> cases = [
        const MapEntry(ConnectionPhase.none, 'Unknown'),
        const MapEntry(ConnectionPhase.connecting, 'Connecting'),
        const MapEntry(ConnectionPhase.negotiating, 'Negotiating'),
        const MapEntry(ConnectionPhase.recovering, 'Recovering'),
      ];

      for (final MapEntry<ConnectionPhase, String> entry in cases) {
        final Store<AppState> store = Store<AppState>(
          appReducer,
          initialState: AppState(
            connection: ConnectionState(
              phase: entry.key,
              session: null,
              error: null,
            ),
          ),
          distinct: true,
        );

        final ConnectionStatusScreenViewModel viewModel =
            ConnectionStatusScreenViewModel.fromStore(store);

        expect(viewModel.statusLabel, entry.value);
      }
    });
  });
}
