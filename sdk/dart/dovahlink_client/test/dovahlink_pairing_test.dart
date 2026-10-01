import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing.dart'
    show DovahLinkPairing;
import 'package:dovahlink_client_sdk/src/internal/pairing/pairing_service.dart'
    show IPairingService;
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart'
    show ISubscriptionService;
import 'fixtures/fixtures.dart';

/// Mocks the existing pairing protocol owner.
class MockClientPairingService extends Mock implements IPairingService {}

/// Mocks desired state subscription restoration.
class MockPairingSubscriptionService extends Mock
    implements ISubscriptionService {}

/// Tests the grouped pairing view over the existing SDK owners.
void main() {
  late MockClientPairingService pairingService;
  late MockPairingSubscriptionService subscriptionService;
  late Stream<List<DovahLinkHost>> candidates;
  late Future<List<DovahLinkHost>> Function() discoverOperation;
  late DovahLinkPairing pairing;
  late int discoveryCalls;
  late List<DovahLinkHost> discoveredHosts;
  final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
  final DovahLinkHostId hostId = DovahLinkHostId(
    '81869993-955c-4ba3-a7d0-d35ca86078ea',
  );

  setUpAll(() {
    registerFallbackValue(uri);
  });

  setUp(() {
    pairingService = MockClientPairingService();
    subscriptionService = MockPairingSubscriptionService();
    candidates = const Stream<List<DovahLinkHost>>.empty();
    discoveryCalls = 0;
    discoveredHosts = <DovahLinkHost>[Fixtures.buildDovahLinkHost()];
    discoverOperation = () async {
      discoveryCalls++;
      return discoveredHosts;
    };
    pairing = DovahLinkPairing(
      discoverHosts: () => discoverOperation(),
      candidates: candidates,
      pairingService: pairingService,
      subscriptionService: subscriptionService,
    );
  });

  group('Method discoverHosts behaves correctly', () {
    test(
      'Method discoverHosts uses the client reconciliation operation',
      () async {
        expect(await pairing.discoverHosts(), discoveredHosts);
        expect(discoveryCalls, 1);
      },
    );

    test('Method discoverHosts propagates typed storage failures', () async {
      const DovahLinkStorageException failure = DovahLinkStorageException(
        'state unavailable',
      );
      discoverOperation = () async => throw failure;

      await expectLater(pairing.discoverHosts(), throwsA(same(failure)));
    });
  });

  group('Method authenticateCandidate behaves correctly', () {
    test(
      'Method authenticateCandidate restores subscriptions after pairing recovery',
      () async {
        final HelloResult hello = HelloResult(
          hostId: Fixtures.buildDovahLinkHost().hostId,
          hostName: 'LOCAL-HOST',
          hostVersion: '1.2.3',
          trustState: DovahLinkTrustState.unpaired,
        );
        final DovahLinkPairingHandshake result = DovahLinkPairingHandshake(
          hello: hello,
          trustState: DovahLinkTrustState.trusted,
        );
        when(
          () => pairingService.authenticateCandidate(any()),
        ).thenAnswer((_) async => result);

        expect(await pairing.authenticateCandidate(uri), same(result));
        verify(() => pairingService.authenticateCandidate(uri)).called(1);
        verify(() => subscriptionService.restoreDesiredStateAreas()).called(1);
      },
    );

    test(
      'Method authenticateCandidate does not restore subscriptions for an unpaired result',
      () async {
        when(() => pairingService.authenticateCandidate(any())).thenAnswer(
          (_) async => DovahLinkPairingHandshake(
            hello: HelloResult(
              hostId: Fixtures.buildDovahLinkHost().hostId,
              hostName: 'LOCAL-HOST',
              hostVersion: '1.2.3',
              trustState: DovahLinkTrustState.unpaired,
            ),
            trustState: DovahLinkTrustState.unpaired,
          ),
        );

        await pairing.authenticateCandidate(uri);

        verifyNever(() => subscriptionService.restoreDesiredStateAreas());
      },
    );

    test(
      'Method authenticateCandidate leaves trusted hello restoration to session admission',
      () async {
        when(() => pairingService.authenticateCandidate(any())).thenAnswer(
          (_) async => DovahLinkPairingHandshake(
            hello: HelloResult(
              hostId: Fixtures.buildDovahLinkHost().hostId,
              hostName: 'LOCAL-HOST',
              hostVersion: '1.2.3',
              trustState: DovahLinkTrustState.trusted,
            ),
            trustState: DovahLinkTrustState.trusted,
          ),
        );

        await pairing.authenticateCandidate(uri);

        verifyNever(() => subscriptionService.restoreDesiredStateAreas());
      },
    );

    test(
      'Method authenticateCandidate propagates errors without restoring subscriptions',
      () async {
        const DovahLinkPairingException failure = DovahLinkPairingException(
          PairingOutcome.pendingNotFound,
        );
        when(
          () => pairingService.authenticateCandidate(any()),
        ).thenAnswer((_) async => throw failure);

        await expectLater(
          pairing.authenticateCandidate(uri),
          throwsA(same(failure)),
        );
        verifyNever(() => subscriptionService.restoreDesiredStateAreas());
      },
    );
  });

  test('Method authenticateKnownHost delegates the exact identity', () async {
    final HelloResult hello = HelloResult(
      hostId: Fixtures.buildDovahLinkHost().hostId,
      hostName: 'LOCAL-HOST',
      hostVersion: '1.2.3',
      trustState: DovahLinkTrustState.trusted,
    );
    when(() => pairingService.authenticateKnownHost(hostId)).thenAnswer(
      (_) async => DovahLinkPairingHandshake(
        hello: hello,
        trustState: DovahLinkTrustState.trusted,
      ),
    );

    await pairing.authenticateKnownHost(hostId);

    verify(() => pairingService.authenticateKnownHost(hostId)).called(1);
    verifyNever(() => subscriptionService.restoreDesiredStateAreas());
  });

  group('Property candidates behaves correctly', () {
    test('Property candidates exposes the SDK candidate projection stream', () {
      expect(identical(pairing.candidates, candidates), isTrue);
    });
  });

  group('Method requestCode behaves correctly', () {
    test(
      'Method requestCode delegates the pairing challenge request',
      () async {
        const PairingChallengeStatus result = PairingChallengeStatus(
          availability: PairingAvailability.available,
          expiresInSeconds: 300,
        );
        when(
          () => pairingService.requestPairing(),
        ).thenAnswer((_) async => result);

        expect(await pairing.requestCode(), same(result));
        verify(() => pairingService.requestPairing()).called(1);
      },
    );

    test('Method requestCode propagates typed protocol failures', () async {
      const DovahLinkProtocolException failure = DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'invalid pairing status',
        retryable: false,
      );
      when(
        () => pairingService.requestPairing(),
      ).thenAnswer((_) async => throw failure);

      await expectLater(pairing.requestCode(), throwsA(same(failure)));
    });
  });

  group('Method renotify behaves correctly', () {
    test(
      'Method renotify delegates the request and preserves cooldown data',
      () async {
        const PairingRenotifyResult result = PairingRenotifyResult(
          status: PairingRenotifyStatus.cooldown,
          retryAfterSeconds: 7,
        );
        when(
          () => pairingService.requestPairingRenotify(),
        ).thenAnswer((_) async => result);

        expect(await pairing.renotify(), same(result));
        verify(() => pairingService.requestPairingRenotify()).called(1);
      },
    );

    test('Method renotify propagates typed pairing failures', () async {
      const DovahLinkPairingException failure = DovahLinkPairingException(
        PairingOutcome.expired,
      );
      when(
        () => pairingService.requestPairingRenotify(),
      ).thenAnswer((_) async => throw failure);

      await expectLater(pairing.renotify(), throwsA(same(failure)));
    });
  });

  group('Method cancel behaves correctly', () {
    test('Method cancel delegates challenge cancellation', () async {
      const PairingCancelOutcome result = PairingCancelOutcome(
        status: PairingCancelStatus.cancelled,
      );
      when(
        () => pairingService.cancelPairing(),
      ).thenAnswer((_) async => result);

      expect(await pairing.cancel(), same(result));
      verify(() => pairingService.cancelPairing()).called(1);
    });

    test('Method cancel propagates typed connection failures', () async {
      const DovahLinkConnectionException failure = DovahLinkConnectionException(
        'session ended',
      );
      when(
        () => pairingService.cancelPairing(),
      ).thenAnswer((_) async => throw failure);

      await expectLater(pairing.cancel(), throwsA(same(failure)));
    });
  });

  group('Method confirmCode behaves correctly', () {
    test('Method confirmCode forwards code and display name', () async {
      when(
        () => pairingService.confirmPairingCodeAndAcknowledge(
          code: '123456',
          displayName: 'Tablet',
        ),
      ).thenAnswer((_) async {});

      await pairing.confirmCode(code: '123456', displayName: 'Tablet');

      verify(
        () => pairingService.confirmPairingCodeAndAcknowledge(
          code: '123456',
          displayName: 'Tablet',
        ),
      ).called(1);
      verify(() => subscriptionService.restoreDesiredStateAreas()).called(1);
    });

    test('Method confirmCode propagates typed persistence failures', () async {
      const DovahLinkStorageException failure = DovahLinkStorageException(
        'credential write failed',
      );
      when(
        () => pairingService.confirmPairingCodeAndAcknowledge(
          code: '123456',
          displayName: 'Tablet',
        ),
      ).thenAnswer((_) async => throw failure);

      await expectLater(
        pairing.confirmCode(code: '123456', displayName: 'Tablet'),
        throwsA(same(failure)),
      );
      verifyNever(() => subscriptionService.restoreDesiredStateAreas());
    });
  });

  group('Method recoverPendingPairing behaves correctly', () {
    test(
      'Method recoverPendingPairing restores subscriptions when trusted',
      () async {
        when(
          () => pairingService.recoverPendingPairing(),
        ).thenAnswer((_) async => DovahLinkTrustState.trusted);

        expect(
          await pairing.recoverPendingPairing(),
          DovahLinkTrustState.trusted,
        );
        verify(() => subscriptionService.restoreDesiredStateAreas()).called(1);
      },
    );

    test(
      'Method recoverPendingPairing keeps subscriptions dormant when unpaired',
      () async {
        when(
          () => pairingService.recoverPendingPairing(),
        ).thenAnswer((_) async => DovahLinkTrustState.unpaired);

        expect(
          await pairing.recoverPendingPairing(),
          DovahLinkTrustState.unpaired,
        );
        verifyNever(() => subscriptionService.restoreDesiredStateAreas());
      },
    );

    test(
      'Method recoverPendingPairing propagates failures without restoring subscriptions',
      () async {
        const DovahLinkStorageException failure = DovahLinkStorageException(
          'recovery state unavailable',
        );
        when(
          () => pairingService.recoverPendingPairing(),
        ).thenAnswer((_) async => throw failure);

        await expectLater(
          pairing.recoverPendingPairing(),
          throwsA(same(failure)),
        );
        verifyNever(() => subscriptionService.restoreDesiredStateAreas());
      },
    );
  });
}
