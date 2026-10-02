import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import '../../fixtures/fixtures.dart';

/// Mocks durable SDK storage for client-state owner tests.
class MockClientStorage extends Mock implements IClientStorage {}

/// Runs persisted client-state ownership behavior tests.
void main() {
  const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
  const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
  late MockClientStorage storage;
  late PersistedClientState persisted;
  late ClientStateService service;

  /// Builds a Host relationship for one test scenario.
  /// @param id The stable Host UUID.
  /// @param name The scenario-specific display name, or the ID by default.
  /// @param credential The credential owned by this Host, if any.
  /// @return A persisted relationship value.
  PersistedKnownHost relationship(
    String id, {
    String? name,
    String? credential,
    bool pairingRequired = false,
  }) => PersistedKnownHost(
    host: Fixtures.buildDovahLinkHost(hostId: id, hostName: name ?? id),
    credential: credential,
    pairingRequired: pairingRequired,
  );

  setUpAll(() {
    registerFallbackValue(PersistedClientState());
  });

  setUp(() {
    storage = MockClientStorage();
    persisted = PersistedClientState(clientId: 'client-1');
    when(() => storage.load()).thenAnswer((_) async => persisted);
    when(() => storage.save(any())).thenAnswer((invocation) async {
      persisted = invocation.positionalArguments.single as PersistedClientState;
    });
    when(() => storage.clear()).thenAnswer((_) async {});
    service = ClientStateService(storage: storage);
  });

  group('Property knownHostsChanges behaves correctly', () {
    test(
      'Property knownHostsChanges emits an empty immutable collection',
      () async {
        final List<PersistedKnownHost> hosts =
            await service.knownHostsChanges.first;

        expect(hosts, isEmpty);
        expect(() => hosts.add(relationship(hostAId)), throwsUnsupportedError);
      },
    );

    test(
      'Property knownHostsChanges emits a sorted complete collection',
      () async {
        persisted = PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{
            hostBId: relationship(hostBId),
            hostAId: relationship(hostAId),
          },
        );
        final List<List<PersistedKnownHost>> emitted = [];
        final StreamSubscription<List<PersistedKnownHost>> subscription =
            service.knownHostsChanges.listen(emitted.add);
        await Future<void>.delayed(Duration.zero);

        expect(emitted.single.map((host) => host.host.hostId), <String>[
          hostAId,
          hostBId,
        ]);
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostsChanges reports a load error and recovers the same listener',
      () async {
        final StateError failure = StateError('storage unavailable');
        int loadCount = 0;
        when(() => storage.load()).thenAnswer((_) async {
          loadCount++;
          if (loadCount == 1) {
            throw failure;
          }
          return PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostAId: relationship(hostAId),
            },
          );
        });
        final List<Object> errors = [];
        final List<List<PersistedKnownHost>> values = [];
        final StreamSubscription<List<PersistedKnownHost>> subscription =
            service.knownHostsChanges.listen(
              values.add,
              onError: (Object error) => errors.add(error),
            );
        await Future<void>.delayed(Duration.zero);
        await service.load();
        await Future<void>.delayed(Duration.zero);

        expect(errors, <Object>[failure]);
        expect(values.single, <PersistedKnownHost>[relationship(hostAId)]);
        expect(loadCount, 2);
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostsChanges emits empty only after a successful retry',
      () async {
        int loadCount = 0;
        when(() => storage.load()).thenAnswer((_) async {
          loadCount++;
          if (loadCount == 1) {
            throw StateError('storage unavailable');
          }
          return PersistedClientState(clientId: 'client-1');
        });
        final List<Object> errors = [];
        final List<List<PersistedKnownHost>> values = [];
        final StreamSubscription<List<PersistedKnownHost>> subscription =
            service.knownHostsChanges.listen(
              values.add,
              onError: (Object error) => errors.add(error),
            );
        await Future<void>.delayed(Duration.zero);

        expect(values, isEmpty);
        await service.load();
        await Future<void>.delayed(Duration.zero);

        expect(errors, hasLength(1));
        expect(values, <List<PersistedKnownHost>>[
          const <PersistedKnownHost>[],
        ]);
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostsChanges does not attach after cancellation during initial load',
      () async {
        final Completer<PersistedClientState> loadGate =
            Completer<PersistedClientState>();
        final Completer<void> loadStarted = Completer<void>();
        when(() => storage.load()).thenAnswer((_) {
          loadStarted.complete();
          return loadGate.future;
        });
        final List<List<PersistedKnownHost>> values = [];
        final StreamSubscription<List<PersistedKnownHost>> subscription =
            service.knownHostsChanges.listen(values.add);
        await loadStarted.future;
        await subscription.cancel();
        loadGate.complete(
          PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostAId: relationship(hostAId),
            },
          ),
        );
        await Future<void>.delayed(Duration.zero);

        expect(values, isEmpty);
        verify(() => storage.load()).called(1);
      },
    );

    test(
      'Property knownHostsChanges publishes only after persistence succeeds',
      () async {
        final List<List<PersistedKnownHost>> values = [];
        final StreamSubscription<List<PersistedKnownHost>> subscription =
            service.knownHostsChanges.listen(values.add);
        await Future<void>.delayed(Duration.zero);
        when(() => storage.save(any())).thenThrow(StateError('save failed'));

        await expectLater(
          service.updateState(
            (PersistedClientState state) => state.copyWith(
              knownHosts: <String, PersistedKnownHost>{
                hostAId: relationship(hostAId),
              },
            ),
          ),
          throwsA(isA<StateError>()),
        );

        expect(values, <List<PersistedKnownHost>>[
          const <PersistedKnownHost>[],
        ]);
        expect(persisted.knownHosts, isEmpty);
        await subscription.cancel();
      },
    );

    test('Property knownHostsChanges skips credential-only changes', () async {
      persisted = PersistedClientState(
        knownHosts: <String, PersistedKnownHost>{
          hostAId: relationship(hostAId, credential: 'credential-a'),
        },
      );
      final List<List<PersistedKnownHost>> values = [];
      final StreamSubscription<List<PersistedKnownHost>> subscription = service
          .knownHostsChanges
          .listen(values.add);
      await Future<void>.delayed(Duration.zero);

      await service.updateState(
        (PersistedClientState state) => state.copyWith(
          knownHosts: <String, PersistedKnownHost>{
            hostAId: relationship(hostAId, credential: 'rotated-credential'),
          },
        ),
      );

      expect(values, <List<PersistedKnownHost>>[
        <PersistedKnownHost>[relationship(hostAId)],
      ]);
      await subscription.cancel();
    });

    test('Property knownHostsChanges publishes a repair-hint change', () async {
      persisted = PersistedClientState(
        knownHosts: <String, PersistedKnownHost>{
          hostAId: relationship(hostAId),
        },
      );
      final List<List<PersistedKnownHost>> values = [];
      final StreamSubscription<List<PersistedKnownHost>> subscription = service
          .knownHostsChanges
          .listen(values.add);
      await Future<void>.delayed(Duration.zero);

      await service.updateState(
        (PersistedClientState state) => state.copyWith(
          knownHosts: <String, PersistedKnownHost>{
            hostAId: relationship(hostAId, pairingRequired: true),
          },
        ),
      );
      await Future<void>.delayed(Duration.zero);

      expect(values, hasLength(2));
      expect(values.last.single.pairingRequired, isTrue);
      expect(values.last.single.credential, isNull);
      await subscription.cancel();
    });

    test(
      'Property knownHostsChanges gives each subscriber the same complete view',
      () async {
        final List<List<PersistedKnownHost>> first = [];
        final List<List<PersistedKnownHost>> second = [];
        final StreamSubscription<List<PersistedKnownHost>> firstSubscription =
            service.knownHostsChanges.listen(first.add);
        final StreamSubscription<List<PersistedKnownHost>> secondSubscription =
            service.knownHostsChanges.listen(second.add);
        await Future<void>.delayed(Duration.zero);
        await service.updateState(
          (PersistedClientState state) => state.copyWith(
            knownHosts: <String, PersistedKnownHost>{
              hostAId: relationship(hostAId, credential: 'credential-a'),
              hostBId: relationship(hostBId, credential: 'credential-b'),
            },
          ),
        );
        await Future<void>.delayed(Duration.zero);

        expect(first, second);
        expect(first.last.map((host) => host.host.hostId), <String>[
          hostAId,
          hostBId,
        ]);
        await firstSubscription.cancel();
        await secondSubscription.cancel();
      },
    );
  });

  test('Method updateState serializes complete-state transactions', () async {
    final Completer<void> saveGate = Completer<void>();
    final Completer<void> saveStarted = Completer<void>();
    when(() => storage.save(any())).thenAnswer((invocation) async {
      if (!saveStarted.isCompleted) {
        saveStarted.complete();
      }
      await saveGate.future;
      persisted = invocation.positionalArguments.single as PersistedClientState;
    });

    final Future<void> first = service.updateState(
      (PersistedClientState state) => state.copyWith(
        knownHosts: <String, PersistedKnownHost>{
          hostAId: relationship(hostAId, credential: 'credential-a'),
        },
      ),
    );
    await saveStarted.future;
    final Future<void> second = service.updateState(
      (PersistedClientState state) => state.copyWith(
        knownHosts: <String, PersistedKnownHost>{
          ...state.knownHosts,
          hostBId: relationship(hostBId, credential: 'credential-b'),
        },
      ),
    );
    saveGate.complete();
    await Future.wait(<Future<void>>[first, second]);

    expect(persisted.knownHosts.keys.toSet(), <String>{hostAId, hostBId});
    expect(persisted.knownHosts[hostAId]?.credential, 'credential-a');
    expect(persisted.knownHosts[hostBId]?.credential, 'credential-b');
    verify(() => storage.save(any())).called(2);
  });
}
