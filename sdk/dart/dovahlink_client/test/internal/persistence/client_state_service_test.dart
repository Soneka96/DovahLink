import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Mocks durable SDK storage for client-state owner tests.
class MockClientStorage extends Mock implements IClientStorage {}

/// Builds a representative persisted Host.
/// @param name The mutable display name to include.
DovahLinkHost buildHost({String name = 'HOST-A'}) => DovahLinkHost(
  hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
  hostName: name,
  endpoint: Uri.parse('ws://127.0.0.1:58231/'),
);

/// Runs persisted client-state ownership behavior tests.
void main() {
  late MockClientStorage storage;
  late PersistedClientState persisted;
  late ClientStateService service;

  setUpAll(() {
    registerFallbackValue(const PersistedClientState());
  });

  setUp(() {
    storage = MockClientStorage();
    persisted = const PersistedClientState(clientId: 'client-1');
    when(() => storage.load()).thenAnswer((_) async => persisted);
    when(() => storage.save(any())).thenAnswer((invocation) async {
      persisted = invocation.positionalArguments.single as PersistedClientState;
    });
    when(() => storage.clear()).thenAnswer((_) async {});
    service = ClientStateService(storage: storage);
  });

  group('Property knownHostChanges behaves correctly', () {
    test(
      'Property knownHostChanges first emits null when no Host is persisted',
      () async {
        expect(await service.knownHostChanges.first, isNull);
        verify(() => storage.load()).called(1);
      },
    );

    test('Property knownHostChanges first emits the persisted Host', () async {
      persisted = PersistedClientState(
        clientId: 'client-1',
        knownHost: buildHost(),
      );

      expect(await service.knownHostChanges.first, buildHost());
    });

    test(
      'Property knownHostChanges reports a load error and retries on a later subscription',
      () async {
        int loadCount = 0;
        when(() => storage.load()).thenAnswer((_) async {
          loadCount++;
          if (loadCount == 1) {
            throw StateError('storage unavailable');
          }
          return PersistedClientState(
            clientId: 'client-1',
            knownHost: buildHost(),
          );
        });

        await expectLater(
          service.knownHostChanges.first,
          throwsA(isA<StateError>()),
        );
        expect(await service.knownHostChanges.first, buildHost());
        expect(loadCount, 2);
      },
    );

    test(
      'Property knownHostChanges replays to multiple subscribers and publishes a commit',
      () async {
        persisted = PersistedClientState(
          clientId: 'client-1',
          knownHost: buildHost(),
        );
        final List<DovahLinkHost?> first = <DovahLinkHost?>[];
        final List<DovahLinkHost?> second = <DovahLinkHost?>[];
        final StreamSubscription<DovahLinkHost?> firstSubscription = service
            .knownHostChanges
            .listen(first.add);
        final StreamSubscription<DovahLinkHost?> secondSubscription = service
            .knownHostChanges
            .listen(second.add);
        await Future<void>.delayed(Duration.zero);

        await service.updateState(
          (PersistedClientState state) =>
              state.copyWith(knownHost: buildHost(name: 'HOST-B')),
        );
        await Future<void>.delayed(Duration.zero);

        expect(first, <DovahLinkHost?>[buildHost(), buildHost(name: 'HOST-B')]);
        expect(second, first);
        await firstSubscription.cancel();
        await secondSubscription.cancel();
      },
    );

    test(
      'Property knownHostChanges publishes nothing when persistence fails',
      () async {
        final List<DovahLinkHost?> values = <DovahLinkHost?>[];
        final StreamSubscription<DovahLinkHost?> subscription = service
            .knownHostChanges
            .listen(values.add);
        await Future<void>.delayed(Duration.zero);
        when(() => storage.save(any())).thenThrow(StateError('save failed'));

        await expectLater(
          service.updateState(
            (PersistedClientState state) =>
                state.copyWith(knownHost: buildHost()),
          ),
          throwsA(isA<StateError>()),
        );
        await Future<void>.delayed(Duration.zero);

        expect(values, <DovahLinkHost?>[null]);
        expect(persisted.knownHost, isNull);
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostChanges does not emit for an equivalent state update',
      () async {
        persisted = PersistedClientState(
          clientId: 'client-1',
          knownHost: buildHost(),
        );
        final List<DovahLinkHost?> values = <DovahLinkHost?>[];
        final StreamSubscription<DovahLinkHost?> subscription = service
            .knownHostChanges
            .listen(values.add);
        await Future<void>.delayed(Duration.zero);

        await service.updateState(
          (PersistedClientState state) =>
              state.copyWith(knownHost: buildHost()),
        );

        expect(values, <DovahLinkHost?>[buildHost()]);
        verifyNever(() => storage.save(any()));
        await subscription.cancel();
      },
    );
  });

  group('Method load behaves correctly', () {
    test(
      'Method load shares the committed state across concurrent callers',
      () async {
        final List<PersistedClientState> states = await Future.wait(
          <Future<PersistedClientState>>[service.load(), service.load()],
        );

        expect(states, <PersistedClientState>[persisted, persisted]);
        verify(() => storage.load()).called(1);
      },
    );
  });

  group('Method updateState behaves correctly', () {
    test('Method updateState serializes complete-state transactions', () async {
      final Completer<void> saveGate = Completer<void>();
      final Completer<void> saveStarted = Completer<void>();
      when(() => storage.save(any())).thenAnswer((invocation) async {
        if (!saveStarted.isCompleted) {
          saveStarted.complete();
        }
        await saveGate.future;
        persisted =
            invocation.positionalArguments.single as PersistedClientState;
      });

      final Future<void> first = service.updateState(
        (PersistedClientState state) =>
            state.copyWith(credential: 'credential-1'),
      );
      await saveStarted.future;
      final Future<void> second = service.updateState(
        (PersistedClientState state) => state.copyWith(knownHost: buildHost()),
      );
      saveGate.complete();
      await Future.wait(<Future<void>>[first, second]);

      expect(persisted.credential, 'credential-1');
      expect(persisted.knownHost, buildHost());
      expect(persisted.recoveryState, PairingRecoveryState.none);
      verify(() => storage.save(any())).called(2);
    });
  });
}
