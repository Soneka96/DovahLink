import 'dart:async';

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

/// Mocks the desired subscription owner.
class MockCurrentHostSubscriptionService extends Mock
    implements ISubscriptionService {}

/// Mocks the grouped Character view.
class MockCurrentHostCharacter extends Mock implements IDovahLinkCharacter {}

/// Tests the current Host view over its session, Character, and subscription owners.
void main() {
  late MockCurrentHostSessionService sessionService;
  late MockCurrentHostSubscriptionService subscriptionService;
  late MockCurrentHostCharacter character;
  late Stream<StateSynchronization<PlayerLocationState?>> playerLocationChanges;
  late Stream<StateSynchronization<GameTimeState?>> gameTimeChanges;
  late Stream<StateSynchronization<TrackedQuestsState?>> trackedQuestsChanges;
  late DovahLinkCurrentHost currentHost;

  setUp(() {
    sessionService = MockCurrentHostSessionService();
    subscriptionService = MockCurrentHostSubscriptionService();
    character = MockCurrentHostCharacter();
    playerLocationChanges =
        Stream<StateSynchronization<PlayerLocationState?>>.value(
          const StateSynchronization<PlayerLocationState?>.notSubscribed(),
        );
    gameTimeChanges = Stream<StateSynchronization<GameTimeState?>>.value(
      const StateSynchronization<GameTimeState?>.notSubscribed(),
    );
    trackedQuestsChanges =
        Stream<StateSynchronization<TrackedQuestsState?>>.value(
          const StateSynchronization<TrackedQuestsState?>.notSubscribed(),
        );
    currentHost = DovahLinkCurrentHost(
      sessionService: sessionService,
      character: character,
      playerLocationChanges: playerLocationChanges,
      gameTimeChanges: gameTimeChanges,
      trackedQuestsChanges: trackedQuestsChanges,
      subscriptionService: subscriptionService,
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

  group('Property character behaves correctly', () {
    test('Property character exposes the grouped Character view', () {
      expect(currentHost.character, same(character));
    });
  });

  group('Property playerLocationChanges behaves correctly', () {
    test(
      'Property playerLocationChanges exposes the synchronization-aware stream',
      () {
        expect(currentHost.playerLocationChanges, same(playerLocationChanges));
      },
    );
  });

  group('Property gameTimeChanges behaves correctly', () {
    test(
      'Property gameTimeChanges exposes the synchronization-aware stream',
      () {
        expect(currentHost.gameTimeChanges, same(gameTimeChanges));
      },
    );
  });

  group('Property trackedQuestsChanges behaves correctly', () {
    test(
      'Property trackedQuestsChanges exposes the synchronization-aware stream',
      () {
        expect(currentHost.trackedQuestsChanges, same(trackedQuestsChanges));
      },
    );
  });

  group('Method subscribeStateArea behaves correctly', () {
    test('Method subscribeStateArea delegates the requested domain', () async {
      const Set<DovahLinkStateArea> rejected = <DovahLinkStateArea>{};
      when(
        () => subscriptionService.subscribeStateArea(
          DovahLinkStateArea.characterVitals,
        ),
      ).thenAnswer((_) async => rejected);

      expect(
        await currentHost.subscribeStateArea(
          DovahLinkStateArea.characterVitals,
        ),
        rejected,
      );
      verify(
        () => subscriptionService.subscribeStateArea(
          DovahLinkStateArea.characterVitals,
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
            DovahLinkStateArea.characterLevel,
          ),
        ).thenAnswer((_) async => rejected);

        expect(
          await currentHost.unsubscribeStateArea(
            DovahLinkStateArea.characterLevel,
          ),
          rejected,
        );
        verify(
          () => subscriptionService.unsubscribeStateArea(
            DovahLinkStateArea.characterLevel,
          ),
        ).called(1);
      },
    );
  });
}
