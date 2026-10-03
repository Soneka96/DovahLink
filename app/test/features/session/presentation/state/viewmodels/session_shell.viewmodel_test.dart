import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.actions.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_shell.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../../fixtures/fixtures.dart';

/// Exercises Session Shell ViewModel projection and callbacks.
void main() {
  group('Method fromStore behaves correctly', () {
    test('fromStore projects the requested Known Host', () {
      final host = Fixtures.buildHost(hostId: 'selected-host');
      final store = const CreateStore()();
      store.dispatch(
        ConnectionKnownHostsChangedAction([
          Fixtures.buildKnownHost(
            host: host,
            availability: HostAvailability.online,
            sessionState: KnownHostSessionState.connected,
          ),
        ]),
      );

      final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
        store,
        hostId: 'selected-host',
      );

      expect(viewModel.host?.host.hostId, 'selected-host');
      expect(viewModel.host?.state, DovahConnectionCardState.connected);
    });

    test('fromStore leaves an absent Known Host unavailable', () {
      final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
        const CreateStore()(),
        hostId: 'missing-host',
      );

      expect(viewModel.host, isNull);
    });
  });

  group('Callback onBack behaves correctly', () {
    test('onBack dispatches the Session Shell navigation request', () {
      final List<Object?> actions = [];
      final Store<AppState> recordingStore = Store<AppState>((
        AppState state,
        Object? action,
      ) {
        actions.add(action);
        return state;
      }, initialState: AppState.initial());
      final SessionShellViewModel viewModel = SessionShellViewModel.fromStore(
        recordingStore,
        hostId: 'missing-host',
      );

      viewModel.onBack();

      expect(actions, [isA<SessionShellBackRequestedAction>()]);
    });
  });
}
