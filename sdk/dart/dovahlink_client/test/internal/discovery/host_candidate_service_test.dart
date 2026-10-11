import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/internal/discovery/host_candidate_service.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import '../../fixtures/fixtures.dart';

/// Mock Known Host owner supplying committed state to the candidate service.
class MockClientStateService extends Mock implements IClientStateService {}

/// Mock discovery contract supplying sessionless Host claims.
class MockDiscoveryService extends Mock implements IDovahLinkDiscoveryService {}

/// Builds persisted state holding [hosts] as Known Hosts.
PersistedClientState _stateWith(List<DovahLinkHost> hosts) =>
    PersistedClientState(
      knownHosts: <String, PersistedKnownHost>{
        for (final DovahLinkHost host in hosts)
          host.hostId: PersistedKnownHost(host: host),
      },
    );

/// Runs host-candidate-service behavior tests.
void main() {
  const String idA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
  const String idB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
  late MockClientStateService clientStateService;
  late MockDiscoveryService discoveryService;
  late StreamController<List<PersistedKnownHost>> knownHostChanges;
  late HostCandidateService service;
  late PersistedClientState committed;

  setUp(() {
    clientStateService = MockClientStateService();
    discoveryService = MockDiscoveryService();
    knownHostChanges = StreamController<List<PersistedKnownHost>>.broadcast();
    committed = _stateWith(const <DovahLinkHost>[]);
    when(() => clientStateService.load()).thenAnswer((_) async => committed);
    when(
      () => clientStateService.knownHostsChanges,
    ).thenAnswer((_) => knownHostChanges.stream);
    service = HostCandidateService(
      clientStateService: clientStateService,
      discoveryService: discoveryService,
    );
    addTearDown(service.close);
    addTearDown(knownHostChanges.close);
  });

  group('Property candidates behaves correctly', () {
    test('Property candidates replays the current empty collection', () async {
      expect(await service.candidates.first, isEmpty);
    });

    test(
      'Property candidates replays the latest result to a late subscriber',
      () async {
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: idA);
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[host]);

        await service.discoverHosts();

        expect(await service.candidates.first, <DovahLinkHost>[host]);
      },
    );

    test(
      'Property candidates removes a candidate when it becomes a Known Host',
      () async {
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: idA);
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[host]);
        final StreamIterator<List<DovahLinkHost>> updates = StreamIterator(
          service.candidates,
        );
        addTearDown(updates.cancel);
        expect(await updates.moveNext(), isTrue);
        await service.discoverHosts();
        expect(await updates.moveNext(), isTrue);
        expect(updates.current, <DovahLinkHost>[host]);

        knownHostChanges.add(<PersistedKnownHost>[
          PersistedKnownHost(host: host),
        ]);

        expect(await updates.moveNext(), isTrue);
        expect(updates.current, isEmpty);
      },
    );

    test('Property candidates forwards a Known Host stream error', () async {
      when(
        () => discoveryService.discover(),
      ).thenAnswer((_) async => <DovahLinkHost>[]);
      final Completer<Object> error = Completer<Object>();
      final StreamSubscription<List<DovahLinkHost>> subscription = service
          .candidates
          .listen((_) {}, onError: error.complete);
      addTearDown(subscription.cancel);
      await service.discoverHosts();

      knownHostChanges.addError(const DovahLinkStorageException('broken'));

      expect(await error.future, isA<DovahLinkStorageException>());
    });
  });

  group('Behavior candidate reconciliation behaves correctly', () {
    test(
      'Behavior a removed Known Host returns to the candidates from the retained discovery',
      () async {
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: idA);
        committed = _stateWith(<DovahLinkHost>[host]);
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[host]);
        expect(await service.discoverHosts(), isEmpty);
        final StreamIterator<List<DovahLinkHost>> updates = StreamIterator(
          service.candidates,
        );
        addTearDown(updates.cancel);
        expect(await updates.moveNext(), isTrue);

        knownHostChanges.add(const <PersistedKnownHost>[]);

        expect(await updates.moveNext(), isTrue);
        expect(updates.current, <DovahLinkHost>[host]);
      },
    );

    test('Behavior an empty re-discovery clears the candidates', () async {
      final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: idA);
      final List<List<DovahLinkHost>> results = <List<DovahLinkHost>>[
        <DovahLinkHost>[host],
        <DovahLinkHost>[],
      ];
      when(
        () => discoveryService.discover(),
      ).thenAnswer((_) async => results.removeAt(0));

      expect(await service.discoverHosts(), <DovahLinkHost>[host]);
      expect(await service.discoverHosts(), isEmpty);
    });

    test('Behavior an unchanged reconciliation does not re-emit', () async {
      final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: idA);
      when(
        () => discoveryService.discover(),
      ).thenAnswer((_) async => <DovahLinkHost>[host]);
      final List<List<DovahLinkHost>> emissions = <List<DovahLinkHost>>[];
      final StreamSubscription<List<DovahLinkHost>> subscription = service
          .candidates
          .listen(emissions.add);
      addTearDown(subscription.cancel);

      await service.discoverHosts();
      await service.discoverHosts();
      await pumpEventQueue();

      expect(emissions, <List<DovahLinkHost>>[
        <DovahLinkHost>[],
        <DovahLinkHost>[host],
      ]);
    });

    test('Behavior the first claim for one normalized Host ID wins', () async {
      final DovahLinkHost first = Fixtures.buildDovahLinkHost(hostId: idA);
      final DovahLinkHost second = Fixtures.buildDovahLinkHost(
        hostId: idA.toUpperCase(),
        endpoint: 'ws://127.0.0.1:58232/',
      );
      when(
        () => discoveryService.discover(),
      ).thenAnswer((_) async => <DovahLinkHost>[first, second]);

      expect(await service.discoverHosts(), <DovahLinkHost>[first]);
    });
  });

  group('Method discoverHosts behaves correctly', () {
    test(
      'Method discoverHosts excludes Known Host IDs regardless of casing and endpoint',
      () async {
        committed = _stateWith(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: idA),
        ]);
        final DovahLinkHost claimedA = Fixtures.buildDovahLinkHost(
          hostId: idA.toUpperCase(),
          endpoint: 'ws://127.0.0.1:58232/',
        );
        final DovahLinkHost candidateB = Fixtures.buildDovahLinkHost(
          hostId: idB,
        );
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[claimedA, candidateB]);

        expect(await service.discoverHosts(), <DovahLinkHost>[candidateB]);
      },
    );

    test(
      'Method discoverHosts sorts by Host ID and suppresses duplicates',
      () async {
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(hostId: idA);
        final DovahLinkHost hostB = Fixtures.buildDovahLinkHost(hostId: idB);
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[hostB, hostA, hostA]);

        expect(await service.discoverHosts(), <DovahLinkHost>[hostA, hostB]);
      },
    );

    test('Method discoverHosts returns an unmodifiable collection', () async {
      when(() => discoveryService.discover()).thenAnswer(
        (_) async => <DovahLinkHost>[Fixtures.buildDovahLinkHost(hostId: idA)],
      );

      final List<DovahLinkHost> result = await service.discoverHosts();

      expect(() => result.clear(), throwsUnsupportedError);
    });

    test(
      'Method discoverHosts ignores an older discovery that finishes last',
      () async {
        final DovahLinkHost older = Fixtures.buildDovahLinkHost(hostId: idA);
        final DovahLinkHost newer = Fixtures.buildDovahLinkHost(hostId: idB);
        final List<Completer<List<DovahLinkHost>>> pending =
            <Completer<List<DovahLinkHost>>>[
              Completer<List<DovahLinkHost>>(),
              Completer<List<DovahLinkHost>>(),
            ];
        int calls = 0;
        final Completer<void> bothStarted = Completer<void>();
        when(() => discoveryService.discover()).thenAnswer((_) {
          final Completer<List<DovahLinkHost>> next = pending[calls++];
          if (calls == 2) {
            bothStarted.complete();
          }
          return next.future;
        });

        final Future<List<DovahLinkHost>> olderDiscovery = service
            .discoverHosts();
        await pumpEventQueue();
        final Future<List<DovahLinkHost>> newerDiscovery = service
            .discoverHosts();
        await bothStarted.future;
        pending[1].complete(<DovahLinkHost>[newer]);
        expect(await newerDiscovery, <DovahLinkHost>[newer]);
        pending[0].complete(<DovahLinkHost>[older]);
        expect(await olderDiscovery, <DovahLinkHost>[newer]);
      },
    );

    test(
      'Method discoverHosts does not publish a claim whose Host became known during discovery',
      () async {
        final DovahLinkHost claimed = Fixtures.buildDovahLinkHost(hostId: idA);
        final Completer<List<DovahLinkHost>> result =
            Completer<List<DovahLinkHost>>();
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) => result.future);

        final Future<List<DovahLinkHost>> pending = service.discoverHosts();
        await pumpEventQueue();
        committed = _stateWith(<DovahLinkHost>[claimed]);
        result.complete(<DovahLinkHost>[claimed]);

        expect(await pending, isEmpty);
      },
    );

    test('Method discoverHosts surfaces a storage failure', () async {
      when(
        () => clientStateService.load(),
      ).thenAnswer((_) async => throw const DovahLinkStorageException('bad'));

      await expectLater(
        service.discoverHosts(),
        throwsA(isA<DovahLinkStorageException>()),
      );
      verifyNever(() => discoveryService.discover());
    });

    test(
      'Method discoverHosts surfaces a failing second read and keeps the previous candidates',
      () async {
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: idA);
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[host]);
        await service.discoverHosts();
        int loads = 0;
        when(() => clientStateService.load()).thenAnswer((_) async {
          if (++loads == 2) {
            throw const DovahLinkStorageException('second read');
          }
          return committed;
        });

        await expectLater(
          service.discoverHosts(),
          throwsA(isA<DovahLinkStorageException>()),
        );
        expect(await service.candidates.first, <DovahLinkHost>[host]);
      },
    );

    test(
      'Method discoverHosts suppresses a failing second read after close',
      () async {
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[]);
        final Completer<PersistedClientState> gate =
            Completer<PersistedClientState>();
        int loads = 0;
        when(() => clientStateService.load()).thenAnswer((_) {
          return ++loads == 2 ? gate.future : Future.value(committed);
        });

        final Future<List<DovahLinkHost>> pending = service.discoverHosts();
        await pumpEventQueue();
        await service.close();
        gate.completeError(const DovahLinkStorageException('late'));

        expect(await pending, isEmpty);
      },
    );

    test('Method discoverHosts surfaces a discovery failure', () async {
      when(
        () => discoveryService.discover(),
      ).thenAnswer((_) async => throw const DovahLinkConnectionException('no'));

      await expectLater(
        service.discoverHosts(),
        throwsA(isA<DovahLinkConnectionException>()),
      );
    });

    test(
      'Method discoverHosts observes Known Host changes only once across calls',
      () async {
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[]);

        await service.discoverHosts();
        await service.discoverHosts();

        verify(() => clientStateService.knownHostsChanges).called(1);
      },
    );

    test('Method discoverHosts throws StateError after close', () async {
      await service.close();

      await expectLater(service.discoverHosts(), throwsStateError);
    });

    test(
      'Method discoverHosts starts nothing when close happens during the first load',
      () async {
        final Completer<PersistedClientState> gate =
            Completer<PersistedClientState>();
        when(() => clientStateService.load()).thenAnswer((_) => gate.future);

        final Future<List<DovahLinkHost>> pending = service.discoverHosts();
        await service.close();
        gate.complete(committed);

        expect(await pending, isEmpty);
        verifyNever(() => clientStateService.knownHostsChanges);
        verifyNever(() => discoveryService.discover());
      },
    );

    test(
      'Method discoverHosts suppresses a storage error that arrives after close',
      () async {
        final Completer<PersistedClientState> gate =
            Completer<PersistedClientState>();
        when(() => clientStateService.load()).thenAnswer((_) => gate.future);

        final Future<List<DovahLinkHost>> pending = service.discoverHosts();
        await service.close();
        gate.completeError(const DovahLinkStorageException('late'));

        expect(await pending, isEmpty);
        verifyNever(() => clientStateService.knownHostsChanges);
      },
    );

    test(
      'Method discoverHosts ignores a discovery result that completes after close',
      () async {
        final Completer<List<DovahLinkHost>> result =
            Completer<List<DovahLinkHost>>();
        when(
          () => discoveryService.discover(),
        ).thenAnswer((_) => result.future);

        final Future<List<DovahLinkHost>> pending = service.discoverHosts();
        await pumpEventQueue();
        await service.close();
        result.complete(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: idA),
        ]);

        expect(await pending, isEmpty);
      },
    );
  });

  group('Method close behaves correctly', () {
    test('Method close is idempotent', () async {
      await service.close();

      await expectLater(service.close(), completes);
    });

    test('Method close cancels the Known Host observation', () async {
      when(
        () => discoveryService.discover(),
      ).thenAnswer((_) async => <DovahLinkHost>[]);
      await service.discoverHosts();
      expect(knownHostChanges.hasListener, isTrue);

      await service.close();

      expect(knownHostChanges.hasListener, isFalse);
    });
  });
}
