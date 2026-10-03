import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/device_identity/domain/usecases/params/set_device_name.params.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/set_device_name.usecase.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkClient,
        DovahLinkConnectionState,
        DovahLinkConnectionException,
        DovahLinkHost,
        DovahLinkTrustState,
        IDovahLinkConnections,
        IDovahLinkCurrentHost,
        RenameOutcome;

/// Mocks the local display-name use case for middleware tests.
class MockSetDeviceNameUseCase extends Mock implements ISetDeviceNameUseCase {}

/// Mocks the SDK client used for active-Host rename.
class MockDeviceIdentityDovahLinkClient extends Mock
    implements DovahLinkClient {}

/// Mocks the SDK connection group used for the trusted-session check and rename.
class MockDeviceIdentityConnections extends Mock
    implements IDovahLinkConnections {}

/// Mocks the SDK current-Host group used for trust and Host identity.
class MockDeviceIdentityCurrentHost extends Mock
    implements IDovahLinkCurrentHost {}

/// Mocks the Redux store so middleware dispatches can be inspected directly.
class MockDeviceIdentityStore extends Mock implements Store<AppState> {}

/// Exercises local save ordering and active-Host rename result handling.
void main() {
  const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
  final DovahLinkHost activeHost = DovahLinkHost(
    hostId: hostId,
    hostName: 'TEST-HOST',
    endpoint: Uri.parse('ws://127.0.0.1:58231/'),
  );

  late DeviceIdentityMiddleware middleware;
  late MockSetDeviceNameUseCase setDeviceName;
  late MockDeviceIdentityDovahLinkClient client;
  late MockDeviceIdentityConnections connections;
  late MockDeviceIdentityCurrentHost currentHost;
  late MockDeviceIdentityStore store;
  late List<Object?> actionLog;
  late List<String> effectOrder;

  void next(dynamic action) => actionLog.add(action);

  setUpAll(() {
    registerFallbackValue(const SetDeviceNameParams(displayName: 'Desktop'));
  });

  setUp(() async {
    await sl.reset();
    middleware = DeviceIdentityMiddleware();
    setDeviceName = MockSetDeviceNameUseCase();
    client = MockDeviceIdentityDovahLinkClient();
    connections = MockDeviceIdentityConnections();
    currentHost = MockDeviceIdentityCurrentHost();
    store = MockDeviceIdentityStore();
    actionLog = <Object?>[];
    effectOrder = <String>[];

    when(() => store.dispatch(any())).thenAnswer((Invocation invocation) {
      final Object? action = invocation.positionalArguments[0];
      actionLog.add(action);
      if (action is DeviceNameSavedAction) {
        effectOrder.add('local-state-updated');
      }
    });
    when(() => client.connections).thenReturn(connections);
    when(() => client.currentHost).thenReturn(currentHost);
    when(
      () => connections.state,
    ).thenReturn(DovahLinkConnectionState.disconnected);
    when(() => currentHost.trustState).thenReturn(null);
    when(() => currentHost.host).thenReturn(null);

    sl.registerSingleton<ISetDeviceNameUseCase>(setDeviceName);
    sl.registerSingleton<DovahLinkClient>(client);
  });

  tearDown(() async {
    await sl.reset();
    reset(setDeviceName);
    reset(client);
    reset(connections);
    reset(currentHost);
    reset(store);
  });

  group('DeviceIdentityMiddleware processes DeviceNameSaveRequestedAction', () {
    test(
      'DeviceNameSaveRequestedAction saves locally before the active Host acknowledgement',
      () async {
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: '  Saved Device  '),
          ),
        ).thenAnswer((_) async {
          effectOrder.add('local-preference-saved');
          return const Right('Saved Device');
        });
        when(
          () => connections.state,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => currentHost.trustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        when(() => currentHost.host).thenReturn(activeHost);
        when(() => connections.renameDevice('Saved Device')).thenAnswer((
          _,
        ) async {
          effectOrder.add('active-host-rename');
          return RenameOutcome.renamed;
        });

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: '  Saved Device  '),
          next,
        );
        await pumpEventQueue();

        expect(actionLog, [
          const DeviceNameSaveRequestedAction(displayName: '  Saved Device  '),
          const DeviceNameSavedAction(displayName: 'Saved Device'),
          const DeviceNameSaveFinishedAction(
            remoteRenameStatus: DeviceNameRenameStatus.renamed,
            remoteHostId: hostId,
          ),
        ]);
        verify(() => connections.renameDevice('Saved Device')).called(1);
        expect(effectOrder, [
          'local-preference-saved',
          'local-state-updated',
          'active-host-rename',
        ]);
      },
    );

    test(
      'DeviceNameSaveRequestedAction updates only the local name without a trusted Host',
      () async {
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'Saved Device'),
          ),
        ).thenAnswer((_) async => const Right('Saved Device'));

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          next,
        );
        await pumpEventQueue();

        expect(actionLog, [
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          const DeviceNameSavedAction(displayName: 'Saved Device'),
          const DeviceNameSaveFinishedAction(
            remoteRenameStatus: DeviceNameRenameStatus.notAttempted,
          ),
        ]);
        verifyNever(() => connections.renameDevice(any()));
      },
    );

    test(
      'DeviceNameSaveRequestedAction skips rename on a connected untrusted session',
      () async {
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'Saved Device'),
          ),
        ).thenAnswer((_) async => const Right('Saved Device'));
        when(
          () => connections.state,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => currentHost.trustState,
        ).thenReturn(DovahLinkTrustState.unpaired);
        when(() => currentHost.host).thenReturn(activeHost);

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          next,
        );
        await pumpEventQueue();

        expect(
          actionLog,
          contains(
            const DeviceNameSaveFinishedAction(
              remoteRenameStatus: DeviceNameRenameStatus.notAttempted,
            ),
          ),
        );
        verifyNever(() => connections.renameDevice(any()));
      },
    );

    test(
      'DeviceNameSaveRequestedAction skips rename when a trusted session lacks a Host identity',
      () async {
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'Saved Device'),
          ),
        ).thenAnswer((_) async => const Right('Saved Device'));
        when(
          () => connections.state,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => currentHost.trustState,
        ).thenReturn(DovahLinkTrustState.trusted);

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          next,
        );
        await pumpEventQueue();

        expect(
          actionLog,
          contains(
            const DeviceNameSaveFinishedAction(
              remoteRenameStatus: DeviceNameRenameStatus.notAttempted,
            ),
          ),
        );
        verifyNever(() => connections.renameDevice(any()));
      },
    );

    test(
      'DeviceNameSaveRequestedAction skips rename for a trusted disconnected Host',
      () async {
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'Saved Device'),
          ),
        ).thenAnswer((_) async => const Right('Saved Device'));
        when(
          () => currentHost.trustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        when(() => currentHost.host).thenReturn(activeHost);

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          next,
        );
        await pumpEventQueue();

        expect(
          actionLog,
          contains(
            const DeviceNameSaveFinishedAction(
              remoteRenameStatus: DeviceNameRenameStatus.notAttempted,
            ),
          ),
        );
        verifyNever(() => connections.renameDevice(any()));
      },
    );

    test(
      'DeviceNameSaveRequestedAction preserves typed Host rename rejections',
      () async {
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'Saved Device'),
          ),
        ).thenAnswer((_) async => const Right('Saved Device'));
        when(
          () => connections.state,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => currentHost.trustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        when(() => currentHost.host).thenReturn(activeHost);
        when(
          () => connections.renameDevice('Saved Device'),
        ).thenAnswer((_) async => RenameOutcome.invalidDisplayName);

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          next,
        );
        await pumpEventQueue();

        expect(
          actionLog,
          contains(
            const DeviceNameSaveFinishedAction(
              remoteRenameStatus: DeviceNameRenameStatus.invalidDisplayName,
              remoteHostId: hostId,
            ),
          ),
        );
        expect(
          actionLog,
          contains(const DeviceNameSavedAction(displayName: 'Saved Device')),
        );
      },
    );

    test(
      'DeviceNameSaveRequestedAction preserves a Host not-trusted outcome',
      () async {
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'Saved Device'),
          ),
        ).thenAnswer((_) async => const Right('Saved Device'));
        when(
          () => connections.state,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => currentHost.trustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        when(() => currentHost.host).thenReturn(activeHost);
        when(
          () => connections.renameDevice('Saved Device'),
        ).thenAnswer((_) async => RenameOutcome.notTrusted);

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          next,
        );
        await pumpEventQueue();

        expect(
          actionLog,
          contains(
            const DeviceNameSaveFinishedAction(
              remoteRenameStatus: DeviceNameRenameStatus.notTrusted,
              remoteHostId: hostId,
            ),
          ),
        );
      },
    );

    test(
      'DeviceNameSaveRequestedAction keeps the local name when Host acknowledgement is lost',
      () async {
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'Saved Device'),
          ),
        ).thenAnswer((_) async => const Right('Saved Device'));
        when(
          () => connections.state,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => currentHost.trustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        when(() => currentHost.host).thenReturn(activeHost);
        when(() => connections.renameDevice('Saved Device')).thenAnswer(
          (_) => Future<RenameOutcome>.error(
            const DovahLinkConnectionException('connection lost during rename'),
          ),
        );

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          next,
        );
        await pumpEventQueue();

        expect(
          actionLog,
          contains(const DeviceNameSavedAction(displayName: 'Saved Device')),
        );
        expect(
          actionLog,
          contains(
            const DeviceNameSaveFinishedAction(
              remoteRenameStatus: DeviceNameRenameStatus.unconfirmed,
              remoteHostId: hostId,
            ),
          ),
        );
      },
    );

    test(
      'DeviceNameSaveRequestedAction reports local persistence failure without renaming',
      () async {
        const DatabaseFailure failure = DatabaseFailure('Storage unavailable.');
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'Saved Device'),
          ),
        ).thenAnswer((_) async => const Left(failure));

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          next,
        );
        await pumpEventQueue();

        expect(actionLog, [
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          const DeviceNameSaveFailedAction(message: 'Storage unavailable.'),
        ]);
        verifyNever(() => connections.renameDevice(any()));
      },
    );

    test(
      'DeviceNameSaveRequestedAction ignores overlapping saves while one is active',
      () async {
        final Completer<Either<Failure, String>> persistence =
            Completer<Either<Failure, String>>();
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'First Name'),
          ),
        ).thenAnswer((_) => persistence.future);

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'First Name'),
          next,
        );
        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Second Name'),
          next,
        );
        persistence.complete(const Right('First Name'));
        await pumpEventQueue();

        verify(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'First Name'),
          ),
        ).called(1);
        expect(actionLog.whereType<DeviceNameSavedAction>(), [
          const DeviceNameSavedAction(displayName: 'First Name'),
        ]);
        verifyNever(() => connections.renameDevice(any()));
      },
    );

    test(
      'DeviceNameSaveRequestedAction maps a thrown local use case to a safe failure',
      () async {
        when(
          () => setDeviceName(
            const SetDeviceNameParams(displayName: 'Saved Device'),
          ),
        ).thenThrow(StateError('internal detail'));

        middleware.call(
          store,
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          next,
        );
        await pumpEventQueue();

        expect(actionLog, [
          const DeviceNameSaveRequestedAction(displayName: 'Saved Device'),
          const DeviceNameSaveFailedAction(
            message: 'The device name could not be saved.',
          ),
        ]);
        verifyNever(() => connections.renameDevice(any()));
      },
    );

    test(
      'DeviceIdentityMiddleware routes unrelated actions without saving',
      () {
        final Object action = Object();

        middleware.call(store, action, next);

        expect(actionLog, [same(action)]);
        verifyNever(() => setDeviceName(any()));
      },
    );
  });
}
