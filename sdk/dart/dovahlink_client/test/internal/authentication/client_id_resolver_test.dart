import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/internal/random_id_generator.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/fixtures.dart';

/// Mocks persisted client-state ownership for resolver tests.
class MockClientStateService extends Mock implements IClientStateService {}

/// Runs client-ID resolver behavior tests.
void main() {
  late MockClientStateService clientStateService;
  late PersistedClientState? savedState;
  late PersistedClientState currentState;

  setUpAll(() {
    registerFallbackValue((PersistedClientState state) => state);
  });

  setUp(() {
    clientStateService = MockClientStateService();
    savedState = null;
    currentState = PersistedClientState();
    when(() => clientStateService.updateState(any())).thenAnswer((
      invocation,
    ) async {
      final PersistedClientState Function(PersistedClientState) update =
          invocation.positionalArguments.single
              as PersistedClientState Function(PersistedClientState);
      savedState = update(currentState);
      currentState = savedState!;
    });
  });

  group('Method resolve behaves correctly', () {
    test(
      'Method resolve reuses an existing persisted client ID without replacing the state',
      () async {
        final PersistedClientState state = Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'credential-1',
          recoveryState: PairingRecoveryState.confirming,
        );
        currentState = state;
        final ClientIdResolver resolver = ClientIdResolver(
          clientStateService: clientStateService,
          randomIdGenerator: RandomIdGenerator(),
        );

        final String clientId = await resolver.resolve(state);

        expect(clientId, 'client-1');
        expect(savedState, isNull);
        verifyNever(() => clientStateService.updateState(any()));
      },
    );

    test(
      'Method resolve generates and persists a client ID on first use',
      () async {
        final PersistedClientState state = Fixtures.buildPersistedClientState(
          clientId: null,
          credential: 'credential-1',
          recoveryState: PairingRecoveryState.confirming,
        );
        final ClientIdResolver resolver = ClientIdResolver(
          clientStateService: clientStateService,
          randomIdGenerator: RandomIdGenerator(),
        );
        currentState = state;

        final String clientId = await resolver.resolve(state);
        final PersistedClientState persisted = savedState!;

        expect(
          clientId,
          matches(
            RegExp(
              r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
            ),
          ),
        );
        expect(persisted.clientId, clientId);
        expect(persisted.knownHosts.values.single.credential, 'credential-1');
        expect(
          persisted.pendingPairingRecovery?.state,
          PairingRecoveryState.confirming,
        );
      },
    );

    test('Method resolve replaces an empty persisted client ID', () async {
      final PersistedClientState state = Fixtures.buildPersistedClientState(
        clientId: '',
        credential: 'credential-1',
        recoveryState: PairingRecoveryState.confirming,
      );
      currentState = state;
      final ClientIdResolver resolver = ClientIdResolver(
        clientStateService: clientStateService,
        randomIdGenerator: RandomIdGenerator(),
      );

      final String clientId = await resolver.resolve(state);

      expect(clientId, isNotEmpty);
      final PersistedClientState persisted = savedState!;
      expect(persisted.clientId, clientId);
      expect(persisted.knownHosts.values.single.credential, 'credential-1');
      expect(
        persisted.pendingPairingRecovery?.state,
        PairingRecoveryState.confirming,
      );
    });

    test(
      'Method resolve returns the one ID persisted by concurrent first use',
      () async {
        final PersistedClientState state = Fixtures.buildPersistedClientState(
          clientId: null,
        );
        currentState = state;
        final ClientIdResolver resolver = ClientIdResolver(
          clientStateService: clientStateService,
          randomIdGenerator: RandomIdGenerator(),
        );

        final List<String> resolved = await Future.wait(<Future<String>>[
          resolver.resolve(state),
          resolver.resolve(state),
        ]);

        expect(resolved, hasLength(2));
        expect(resolved.first, resolved.last);
        expect(savedState?.clientId, resolved.first);
      },
    );
  });
}
