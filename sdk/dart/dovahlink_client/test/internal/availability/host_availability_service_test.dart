import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_state.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/fixtures.dart';

/// Mocks durable state for availability-owner stream tests.
class MockClientStateService extends Mock implements IClientStateService {}

/// Runs runtime Known Host availability ownership tests.
void main() {
  const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
  const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
  late MockClientStateService clientStateService;
  late StreamController<List<DovahLinkHost>> knownHostsController;
  late HostAvailabilityService service;

  setUp(() {
    clientStateService = MockClientStateService();
    knownHostsController = StreamController<List<DovahLinkHost>>.broadcast();
    when(
      () => clientStateService.knownHostsChanges,
    ).thenAnswer((_) => knownHostsController.stream);
    service = HostAvailabilityService(clientStateService: clientStateService);
  });

  tearDown(() async {
    await service.close();
    await knownHostsController.close();
  });

  group('Property knownHostStatesChanges behaves correctly', () {
    test(
      'Property knownHostStatesChanges projects session state only onto its exact Known Host',
      () async {
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final DovahLinkHost hostB = Fixtures.buildDovahLinkHost(
          hostId: hostBId,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            service.knownHostStatesChanges.listen(snapshots.add);
        knownHostsController.add(<DovahLinkHost>[hostA, hostB]);
        await Future<void>.delayed(Duration.zero);

        service.setSessionState(
          DovahLinkHostId(hostAId),
          DovahLinkKnownHostSessionState.connecting,
        );
        service.setSessionState(
          DovahLinkHostId(hostAId),
          DovahLinkKnownHostSessionState.connected,
        );
        await Future<void>.delayed(Duration.zero);

        expect(snapshots.last, <DovahLinkKnownHostState>[
          Fixtures.buildDovahLinkKnownHostState(
            host: hostA,
            sessionState: DovahLinkKnownHostSessionState.connected,
          ),
          Fixtures.buildDovahLinkKnownHostState(host: hostB),
        ]);

        service.setSessionState(
          null,
          DovahLinkKnownHostSessionState.disconnected,
        );
        await Future<void>.delayed(Duration.zero);
        expect(
          snapshots.last.first.sessionState,
          DovahLinkKnownHostSessionState.disconnected,
        );
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges emits complete immutable unknown snapshots and replays them',
      () async {
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final DovahLinkHost hostB = Fixtures.buildDovahLinkHost(
          hostId: hostBId,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            service.knownHostStatesChanges.listen(snapshots.add);
        knownHostsController.add(<DovahLinkHost>[hostA, hostB]);
        await Future<void>.delayed(Duration.zero);

        expect(snapshots, <List<DovahLinkKnownHostState>>[
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(host: hostA),
            Fixtures.buildDovahLinkKnownHostState(host: hostB),
          ],
        ]);
        expect(
          () => snapshots.single.add(Fixtures.buildDovahLinkKnownHostState()),
          throwsUnsupportedError,
        );
        expect(await service.knownHostStatesChanges.first, snapshots.single);
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges emits semantic complete updates without duplicates',
      () async {
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final DovahLinkHost hostB = Fixtures.buildDovahLinkHost(
          hostId: hostBId,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            service.knownHostStatesChanges.listen(snapshots.add);
        knownHostsController.add(<DovahLinkHost>[hostA, hostB]);
        await Future<void>.delayed(Duration.zero);

        service.setAvailability(
          DovahLinkHostId(hostAId),
          DovahLinkHostAvailability.online,
        );
        service.setAvailability(
          DovahLinkHostId(hostAId),
          DovahLinkHostAvailability.online,
        );
        await Future<void>.delayed(Duration.zero);
        service.setAvailability(
          DovahLinkHostId(hostBId),
          DovahLinkHostAvailability.offline,
        );
        await Future<void>.delayed(Duration.zero);
        service.setAvailability(
          DovahLinkHostId(hostAId),
          DovahLinkHostAvailability.unknown,
        );
        await Future<void>.delayed(Duration.zero);

        expect(snapshots, <List<DovahLinkKnownHostState>>[
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(host: hostA),
            Fixtures.buildDovahLinkKnownHostState(host: hostB),
          ],
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(
              host: hostA,
              availability: DovahLinkHostAvailability.online,
            ),
            Fixtures.buildDovahLinkKnownHostState(host: hostB),
          ],
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(
              host: hostA,
              availability: DovahLinkHostAvailability.online,
            ),
            Fixtures.buildDovahLinkKnownHostState(
              host: hostB,
              availability: DovahLinkHostAvailability.offline,
            ),
          ],
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(host: hostA),
            Fixtures.buildDovahLinkKnownHostState(
              host: hostB,
              availability: DovahLinkHostAvailability.offline,
            ),
          ],
        ]);
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges replays storage failure to late subscribers until recovery',
      () async {
        service.setAvailability(
          DovahLinkHostId(hostAId),
          DovahLinkHostAvailability.online,
        );
        final StateError failure = StateError('storage read failed');
        knownHostsController.addError(failure, StackTrace.current);
        await Future<void>.delayed(Duration.zero);

        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final List<Object> errors = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            service.knownHostStatesChanges.listen(
              snapshots.add,
              onError: (Object error) => errors.add(error),
            );
        await Future<void>.delayed(Duration.zero);

        expect(snapshots, isEmpty);
        expect(errors, <Object>[failure]);
        knownHostsController.add(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostAId),
        ]);
        await Future<void>.delayed(Duration.zero);

        expect(
          snapshots.single.single.availability,
          DovahLinkHostAvailability.online,
        );
        expect(errors, <Object>[failure]);
        final List<List<DovahLinkKnownHostState>> recoveredSnapshots = [];
        final List<Object> recoveredErrors = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> recovered =
            service.knownHostStatesChanges.listen(
              recoveredSnapshots.add,
              onError: (Object error) => recoveredErrors.add(error),
            );
        await Future<void>.delayed(Duration.zero);

        expect(recoveredSnapshots, <List<DovahLinkKnownHostState>>[
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(
              host: Fixtures.buildDovahLinkHost(hostId: hostAId),
              availability: DovahLinkHostAvailability.online,
            ),
          ],
        ]);
        expect(recoveredErrors, isEmpty);
        await subscription.cancel();
        await recovered.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges gives multiple subscribers coherent snapshots',
      () async {
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final List<List<DovahLinkKnownHostState>> firstSnapshots = [];
        final List<List<DovahLinkKnownHostState>> secondSnapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> first = service
            .knownHostStatesChanges
            .listen(firstSnapshots.add);
        final StreamSubscription<List<DovahLinkKnownHostState>> second = service
            .knownHostStatesChanges
            .listen(secondSnapshots.add);
        knownHostsController.add(<DovahLinkHost>[hostA]);
        await Future<void>.delayed(Duration.zero);

        service.setAvailability(
          DovahLinkHostId(hostAId),
          DovahLinkHostAvailability.online,
        );
        await Future<void>.delayed(Duration.zero);

        expect(firstSnapshots, secondSnapshots);
        expect(firstSnapshots, <List<DovahLinkKnownHostState>>[
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(host: hostA),
          ],
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(
              host: hostA,
              availability: DovahLinkHostAvailability.online,
            ),
          ],
        ]);
        verify(() => clientStateService.knownHostsChanges).called(1);
        await first.cancel();
        await second.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges preserves availability on metadata refresh and clears removed Hosts',
      () async {
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final DovahLinkHost renamedHostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
          hostName: 'RENAMED-HOST',
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            service.knownHostStatesChanges.listen(snapshots.add);
        knownHostsController.add(<DovahLinkHost>[hostA]);
        await Future<void>.delayed(Duration.zero);
        service.setAvailability(
          DovahLinkHostId(hostAId),
          DovahLinkHostAvailability.online,
        );
        await Future<void>.delayed(Duration.zero);

        knownHostsController.add(<DovahLinkHost>[renamedHostA]);
        await Future<void>.delayed(Duration.zero);
        knownHostsController.add(const <DovahLinkHost>[]);
        await Future<void>.delayed(Duration.zero);
        knownHostsController.add(<DovahLinkHost>[renamedHostA]);
        await Future<void>.delayed(Duration.zero);

        expect(snapshots, <List<DovahLinkKnownHostState>>[
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(host: hostA),
          ],
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(
              host: hostA,
              availability: DovahLinkHostAvailability.online,
            ),
          ],
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(
              host: renamedHostA,
              availability: DovahLinkHostAvailability.online,
            ),
          ],
          const <DovahLinkKnownHostState>[],
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(host: renamedHostA),
          ],
        ]);
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges reports storage errors and recovers without losing the last projection',
      () async {
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final List<Object> errors = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            service.knownHostStatesChanges.listen(
              snapshots.add,
              onError: (Object error) => errors.add(error),
            );
        knownHostsController.add(<DovahLinkHost>[hostA]);
        await Future<void>.delayed(Duration.zero);
        final StateError failure = StateError('storage read failed');
        knownHostsController.addError(failure, StackTrace.current);
        await Future<void>.delayed(Duration.zero);

        expect(errors, <Object>[failure]);
        expect(snapshots, <List<DovahLinkKnownHostState>>[
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(host: hostA),
          ],
        ]);
        knownHostsController.add(<DovahLinkHost>[hostA]);
        await Future<void>.delayed(Duration.zero);

        expect(snapshots, <List<DovahLinkKnownHostState>>[
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(host: hostA),
          ],
          <DovahLinkKnownHostState>[
            Fixtures.buildDovahLinkKnownHostState(host: hostA),
          ],
        ]);
        expect(errors, <Object>[failure]);
        await subscription.cancel();
      },
    );
  });

  group('Method setAvailability behaves correctly', () {
    test(
      'Method setAvailability changes runtime state without persisting it and a new owner starts unknown',
      () async {
        final InMemoryClientStorage storage = InMemoryClientStorage();
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final PersistedClientState persisted = PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{
            hostAId: PersistedKnownHost(host: hostA, credential: 'secret'),
          },
        );
        await storage.save(persisted);
        final ClientStateService realClientStateService = ClientStateService(
          storage: storage,
        );
        final HostAvailabilityService firstService = HostAvailabilityService(
          clientStateService: realClientStateService,
        );
        final StreamIterator<List<DovahLinkKnownHostState>> firstStates =
            StreamIterator(firstService.knownHostStatesChanges);
        expect(await firstStates.moveNext(), isTrue);
        expect(
          firstStates.current.single.availability,
          DovahLinkHostAvailability.unknown,
        );

        firstService.setAvailability(
          DovahLinkHostId(hostAId),
          DovahLinkHostAvailability.offline,
        );
        expect(await firstStates.moveNext(), isTrue);
        expect(
          firstStates.current.single.availability,
          DovahLinkHostAvailability.offline,
        );
        expect(await storage.load(), persisted);

        final HostAvailabilityService restartedService =
            HostAvailabilityService(clientStateService: realClientStateService);
        final List<DovahLinkKnownHostState> restarted =
            await restartedService.knownHostStatesChanges.first;
        expect(
          restarted.single.availability,
          DovahLinkHostAvailability.unknown,
        );
        await firstStates.cancel();
      },
    );
  });

  group('Method close behaves correctly', () {
    test(
      'Method close cancels storage observation and closes the projection stream',
      () async {
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            service.knownHostStatesChanges.listen((_) {});
        await Future<void>.delayed(Duration.zero);
        expect(knownHostsController.hasListener, isTrue);

        await service.close();

        expect(knownHostsController.hasListener, isFalse);
        await subscription.cancel();
        await service.close();
      },
    );
  });
}
