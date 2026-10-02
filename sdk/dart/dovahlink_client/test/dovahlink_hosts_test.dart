import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_hosts.dart'
    show DovahLinkHosts;
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart'
    show IHostAvailabilityService;
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart'
    show IClientStateService;
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'fixtures/fixtures.dart';

/// Mocks the existing durable client-state owner.
class MockHostsClientStateService extends Mock implements IClientStateService {}

/// Mocks the existing runtime Known Host projection owner.
class MockHostsAvailabilityService extends Mock
    implements IHostAvailabilityService {}

/// Tests the public Known Host view over the existing SDK state owners.
void main() {
  late MockHostsClientStateService clientStateService;
  late MockHostsAvailabilityService availabilityService;
  late DovahLinkHosts hosts;

  setUp(() {
    clientStateService = MockHostsClientStateService();
    availabilityService = MockHostsAvailabilityService();
    hosts = DovahLinkHosts(
      clientStateService: clientStateService,
      hostAvailabilityService: availabilityService,
    );
  });

  group('Method loadKnownHosts behaves correctly', () {
    test(
      'Method loadKnownHosts returns an immutable Host-ID-sorted snapshot',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final DovahLinkHost hostB = Fixtures.buildDovahLinkHost(
          hostId: hostBId,
        );
        when(() => clientStateService.load()).thenAnswer(
          (_) async => PersistedClientState(
            knownHosts: <String, PersistedKnownHost>{
              hostBId: PersistedKnownHost(
                host: hostB,
                credential: 'host-b-secret',
              ),
              hostAId: PersistedKnownHost(
                host: hostA,
                credential: 'host-a-secret',
              ),
            },
          ),
        );

        final List<DovahLinkHost> result = await hosts.loadKnownHosts();

        expect(result, <DovahLinkHost>[hostA, hostB]);
        expect(
          () => result.add(Fixtures.buildDovahLinkHost()),
          throwsUnsupportedError,
        );
        verify(() => clientStateService.load()).called(1);
      },
    );

    test('Method loadKnownHosts propagates typed storage failures', () async {
      const DovahLinkStorageException failure = DovahLinkStorageException(
        'corrupt state',
      );
      when(() => clientStateService.load()).thenThrow(failure);

      await expectLater(hosts.loadKnownHosts(), throwsA(same(failure)));
    });
  });

  group('Property knownHostsChanges behaves correctly', () {
    test(
      'Property knownHostsChanges exposes metadata without repair hints',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: hostId);
        final Stream<List<PersistedKnownHost>> source = Stream.fromIterable(
          <List<PersistedKnownHost>>[
            <PersistedKnownHost>[
              PersistedKnownHost(host: host, pairingRequired: true),
            ],
            <PersistedKnownHost>[
              PersistedKnownHost(host: host, pairingRequired: false),
            ],
          ],
        );
        when(
          () => clientStateService.knownHostsChanges,
        ).thenAnswer((_) => source);

        expect(await hosts.knownHostsChanges.toList(), <List<DovahLinkHost>>[
          <DovahLinkHost>[host],
        ]);
        verify(() => clientStateService.knownHostsChanges).called(1);
      },
    );
  });

  group('Property knownHostStatesChanges behaves correctly', () {
    test(
      'Property knownHostStatesChanges exposes the runtime projection stream',
      () {
        const Stream<List<DovahLinkKnownHostState>> changes =
            Stream<List<DovahLinkKnownHostState>>.empty();
        when(
          () => availabilityService.knownHostStatesChanges,
        ).thenAnswer((_) => changes);

        expect(identical(hosts.knownHostStatesChanges, changes), isTrue);
        verify(() => availabilityService.knownHostStatesChanges).called(1);
      },
    );
  });
}
