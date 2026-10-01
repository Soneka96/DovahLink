import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_current_host.dart'
    show DovahLinkCurrentHost;
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart'
    show ISessionService;
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart'
    show ISubscriptionService;
import 'fixtures/fixtures.dart';

/// Mocks the admitted session context owner.
class MockCurrentHostSessionService extends Mock implements ISessionService {}

/// Mocks the existing state subscription owner.
class MockCurrentHostSubscriptionService extends Mock
    implements ISubscriptionService {}

/// Tests the current Host view over the existing session and state owners.
void main() {
  late MockCurrentHostSessionService sessionService;
  late MockCurrentHostSubscriptionService subscriptionService;
  late Stream<StateSynchronization<CharacterXpState>> characterXpChanges;
  late Stream<StateSynchronization<CharacterHealthState>>
  characterHealthChanges;
  late Stream<StateSynchronization<CharacterMagickaState>>
  characterMagickaChanges;
  late Stream<StateSynchronization<CharacterStaminaState>>
  characterStaminaChanges;
  late Stream<StateSynchronization<CharacterLevelState>> characterLevelChanges;
  late DovahLinkCurrentHost currentHost;

  setUp(() {
    sessionService = MockCurrentHostSessionService();
    subscriptionService = MockCurrentHostSubscriptionService();
    characterXpChanges =
        const Stream<StateSynchronization<CharacterXpState>>.empty();
    characterHealthChanges =
        const Stream<StateSynchronization<CharacterHealthState>>.empty();
    characterMagickaChanges =
        const Stream<StateSynchronization<CharacterMagickaState>>.empty();
    characterStaminaChanges =
        const Stream<StateSynchronization<CharacterStaminaState>>.empty();
    characterLevelChanges =
        const Stream<StateSynchronization<CharacterLevelState>>.empty();
    currentHost = DovahLinkCurrentHost(
      sessionService: sessionService,
      subscriptionService: subscriptionService,
      characterXpChanges: characterXpChanges,
      characterHealthChanges: characterHealthChanges,
      characterMagickaChanges: characterMagickaChanges,
      characterStaminaChanges: characterStaminaChanges,
      characterLevelChanges: characterLevelChanges,
    );
  });

  group('Behavior admitted current Host context behaves correctly', () {
    test('Property host and session details read the SDK session owner', () {
      final DovahLinkHost host = Fixtures.buildDovahLinkHost();
      when(() => sessionService.currentHost).thenReturn(host);
      when(
        () => sessionService.currentTrustState,
      ).thenReturn(DovahLinkTrustState.unpaired);
      when(() => sessionService.currentSessionId).thenReturn('session-1');

      expect(currentHost.host, same(host));
      expect(currentHost.trustState, DovahLinkTrustState.unpaired);
      expect(currentHost.sessionId, 'session-1');
      verify(() => sessionService.currentHost).called(1);
      verify(() => sessionService.currentTrustState).called(1);
      verify(() => sessionService.currentSessionId).called(1);
    });
  });

  group('Property characterXpChanges behaves correctly', () {
    test('Property characterXpChanges exposes the XP tracker stream', () {
      expect(
        identical(currentHost.characterXpChanges, characterXpChanges),
        isTrue,
      );
    });
  });

  group('Property characterHealthChanges behaves correctly', () {
    test(
      'Property characterHealthChanges exposes the health tracker stream',
      () {
        expect(
          identical(currentHost.characterHealthChanges, characterHealthChanges),
          isTrue,
        );
      },
    );
  });

  group('Property characterMagickaChanges behaves correctly', () {
    test(
      'Property characterMagickaChanges exposes the magicka tracker stream',
      () {
        expect(
          identical(
            currentHost.characterMagickaChanges,
            characterMagickaChanges,
          ),
          isTrue,
        );
      },
    );
  });

  group('Property characterStaminaChanges behaves correctly', () {
    test(
      'Property characterStaminaChanges exposes the stamina tracker stream',
      () {
        expect(
          identical(
            currentHost.characterStaminaChanges,
            characterStaminaChanges,
          ),
          isTrue,
        );
      },
    );
  });

  group('Property characterLevelChanges behaves correctly', () {
    test('Property characterLevelChanges exposes the level tracker stream', () {
      expect(
        identical(currentHost.characterLevelChanges, characterLevelChanges),
        isTrue,
      );
    });
  });

  group('Method subscribeStateArea behaves correctly', () {
    test('Method subscribeStateArea delegates the requested domain', () async {
      const Set<DovahLinkStateArea> rejected = <DovahLinkStateArea>{};
      when(
        () => subscriptionService.subscribeStateArea(
          DovahLinkStateArea.characterXp,
        ),
      ).thenAnswer((_) async => rejected);

      expect(
        await currentHost.subscribeStateArea(DovahLinkStateArea.characterXp),
        rejected,
      );
      verify(
        () => subscriptionService.subscribeStateArea(
          DovahLinkStateArea.characterXp,
        ),
      ).called(1);
    });

    test(
      'Method subscribeStateArea propagates typed protocol failures',
      () async {
        const DovahLinkProtocolException failure = DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'malformed acknowledgement',
          retryable: false,
        );
        when(
          () => subscriptionService.subscribeStateArea(
            DovahLinkStateArea.characterXp,
          ),
        ).thenAnswer((_) async => throw failure);

        await expectLater(
          currentHost.subscribeStateArea(DovahLinkStateArea.characterXp),
          throwsA(same(failure)),
        );
      },
    );
  });

  group('Method unsubscribeStateArea behaves correctly', () {
    test(
      'Method unsubscribeStateArea delegates the requested domain',
      () async {
        const Set<DovahLinkStateArea> rejected = <DovahLinkStateArea>{};
        when(
          () => subscriptionService.unsubscribeStateArea(
            DovahLinkStateArea.characterXp,
          ),
        ).thenAnswer((_) async => rejected);

        expect(
          await currentHost.unsubscribeStateArea(
            DovahLinkStateArea.characterXp,
          ),
          rejected,
        );
        verify(
          () => subscriptionService.unsubscribeStateArea(
            DovahLinkStateArea.characterXp,
          ),
        ).called(1);
      },
    );

    test(
      'Method unsubscribeStateArea propagates typed connection failures',
      () async {
        const DovahLinkConnectionException failure =
            DovahLinkConnectionException('no trusted session');
        when(
          () => subscriptionService.unsubscribeStateArea(
            DovahLinkStateArea.characterXp,
          ),
        ).thenAnswer((_) async => throw failure);

        await expectLater(
          currentHost.unsubscribeStateArea(DovahLinkStateArea.characterXp),
          throwsA(same(failure)),
        );
      },
    );
  });
}
