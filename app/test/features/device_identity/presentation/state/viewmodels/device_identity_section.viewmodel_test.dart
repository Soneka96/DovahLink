import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/viewmodels/device_identity_section.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../../../../../fixtures/fixtures.dart';

/// Mock Redux store used to build and exercise the section ViewModel.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises the Settings device-identity Redux projection.
void main() {
  late MockStore store;

  setUp(() {
    store = MockStore();
    when(() => store.state).thenReturn(AppState.initial());
  });

  tearDown(() => reset(store));

  group('Method fromStore behaves correctly', () {
    test('Method fromStore projects the resolved device identity', () {
      final DeviceIdentitySectionViewModel viewModel =
          DeviceIdentitySectionViewModel.fromStore(store);

      expect(viewModel.displayName, isA<String>());
      expect(viewModel.displayName, defaultDeviceName);
      expect(viewModel.loadFailure, isNull);
      expect(viewModel.isSaving, isFalse);
      expect(viewModel.saveFailure, isNull);
      expect(viewModel.remoteRenameStatus, isNull);
      expect(viewModel.remoteHostId, isNull);
      expect(viewModel.admittedHostId, isNull);
    });

    test(
      'Method fromStore projects the rename and admitted Host identities',
      () {
        final host = Fixtures.buildHost();
        when(() => store.state).thenReturn(
          AppState(
            connection: ConnectionState(
              knownHosts: [
                Fixtures.buildKnownHost(
                  host: host,
                  sessionState: KnownHostSessionState.connected,
                ),
              ],
            ),
            pairing: PairingState.initial(),
            deviceIdentity: DeviceIdentityState(
              displayName: 'Desktop',
              remoteRenameStatus: DeviceNameRenameStatus.renamed,
              remoteHostId: host.hostId,
            ),
          ),
        );

        final DeviceIdentitySectionViewModel viewModel =
            DeviceIdentitySectionViewModel.fromStore(store);

        expect(viewModel.remoteHostId, host.hostId);
        expect(viewModel.admittedHostId, host.hostId);
      },
    );

    test('Method fromStore dispatches the raw name as a save request', () {
      final List<dynamic> actions = [];
      when(() => store.dispatch(any())).thenAnswer(
        (Invocation invocation) =>
            actions.add(invocation.positionalArguments.single),
      );
      final DeviceIdentitySectionViewModel viewModel =
          DeviceIdentitySectionViewModel.fromStore(store);

      viewModel.onSave('  Gaming PC  ');

      expect(actions, [
        const DeviceNameSaveRequestedAction(displayName: '  Gaming PC  '),
      ]);
    });
  });
}
