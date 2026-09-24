import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_message_handler.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/fixtures.dart';
import 'controlled_request_service.dart';
import 'mock_session_service.dart';

/// Records the accepted state areas given to the state-message gate.
class RecordingStateMessageHandler implements IStateMessageHandler {
  /// Creates a handler with no active state areas.
  RecordingStateMessageHandler();

  /// The accepted areas from the most recent Host acknowledgement.
  Set<String> subscribedStateAreas = <String>{};

  /// Replaces the accepted areas used by this test double.
  /// @param stateAreas The complete accepted set.
  @override
  void setSubscribedStateAreas(Set<String> stateAreas) {
    subscribedStateAreas = Set<String>.of(stateAreas);
  }

  /// Does not route state messages in subscription-service tests.
  /// @param envelope The unused state envelope.
  @override
  void handle(Envelope envelope) {}
}

/// Builds one correlated `subscription_ack` for [accepted] and [rejected].
/// @param accepted The areas the Host accepted.
/// @param rejected The areas the Host rejected.
/// @return A typed acknowledgement envelope.
Envelope _acknowledgement({
  required List<String> accepted,
  List<String> rejected = const <String>[],
}) => Fixtures.buildEnvelope(
  messageType: ProtocolMessageType.subscriptionAck,
  payload: <String, dynamic>{
    'acceptedStateAreas': accepted,
    'rejectedStateAreas': rejected,
  },
);

/// Runs subscription-service behavior tests.
void main() {
  late ControlledRequestService requestService;
  late MockSessionService sessionService;
  late RecordingStateMessageHandler stateMessageHandler;
  late ISubscriptionService service;

  setUpAll(() {
    registerFallbackValue(Exception('fallback for protocol violation'));
  });

  setUp(() {
    requestService = ControlledRequestService();
    sessionService = MockSessionService();
    stateMessageHandler = RecordingStateMessageHandler();
    when(
      () => sessionService.onProtocolViolation(
        any(),
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
      ),
    ).thenAnswer((_) {});
    service = SubscriptionService(
      requestService: requestService,
      sessionService: sessionService,
      stateMessageHandler: stateMessageHandler,
    );
  });

  group('Method synchronizeDesiredStateAreas behaves correctly', () {
    test(
      'Methods subscribe and unsubscribe send successive complete sets',
      () async {
        final Future<Set<DovahLinkStateArea>> first = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        expect(requestService.requests.single.payload, <String, dynamic>{
          'stateAreas': <String>['character_xp'],
        });
        expect(requestService.requests.single.policy.retrySafe, isTrue);
        expect(
          requestService.requests.single.policy.requiredTrustState,
          DovahLinkTrustState.trusted,
        );
        requestService.requests.single.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        expect(await first, isEmpty);
        expect(stateMessageHandler.subscribedStateAreas, <String>{
          'character_xp',
        });

        final Future<Set<DovahLinkStateArea>> second = service
            .subscribeStateArea(DovahLinkStateArea.characterHealth);
        expect(requestService.requests.last.payload, <String, dynamic>{
          'stateAreas': <String>['character_xp', 'character_health'],
        });
        requestService.requests.last.reply.complete(
          _acknowledgement(
            accepted: <String>['character_xp', 'character_health'],
          ),
        );
        expect(await second, isEmpty);

        final Future<Set<DovahLinkStateArea>> third = service
            .unsubscribeStateArea(DovahLinkStateArea.characterXp);
        expect(requestService.requests.last.payload, <String, dynamic>{
          'stateAreas': <String>['character_health'],
        });
        requestService.requests.last.reply.complete(
          _acknowledgement(accepted: <String>['character_health']),
        );
        expect(await third, isEmpty);

        final Future<Set<DovahLinkStateArea>> fourth = service
            .unsubscribeStateArea(DovahLinkStateArea.characterHealth);
        expect(requestService.requests.last.payload, <String, dynamic>{
          'stateAreas': <String>[],
        });
        requestService.requests.last.reply.complete(
          _acknowledgement(accepted: const <String>[]),
        );
        expect(await fourth, isEmpty);
        expect(stateMessageHandler.subscribedStateAreas, isEmpty);
        expect(service.desiredStateAreas, isEmpty);
      },
    );

    test(
      'Method subscribe retains rejected domains as desired intent',
      () async {
        final Future<Set<DovahLinkStateArea>> update = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests.single.reply.complete(
          _acknowledgement(
            accepted: const <String>[],
            rejected: <String>['character_xp'],
          ),
        );

        expect(await update, <DovahLinkStateArea>{
          DovahLinkStateArea.characterXp,
        });
        expect(service.desiredStateAreas, <DovahLinkStateArea>{
          DovahLinkStateArea.characterXp,
        });
        expect(stateMessageHandler.subscribedStateAreas, isEmpty);
      },
    );

    test(
      'Method subscribe retains intent after request failure for later synchronization',
      () async {
        final Future<Set<DovahLinkStateArea>> failedUpdate = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests.single.reply.completeError(
          const DovahLinkConnectionException('Connection lost.'),
        );

        await expectLater(
          failedUpdate,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(service.desiredStateAreas, <DovahLinkStateArea>{
          DovahLinkStateArea.characterXp,
        });

        final Future<Set<DovahLinkStateArea>> retry = service
            .synchronizeDesiredStateAreas();
        expect(requestService.requests.last.payload, <String, dynamic>{
          'stateAreas': <String>['character_xp'],
        });
        requestService.requests.last.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        expect(await retry, isEmpty);
        expect(stateMessageHandler.subscribedStateAreas, <String>{
          'character_xp',
        });
      },
    );

    test(
      'Methods subscribe and unsubscribe remain idempotent when repeated',
      () async {
        final Future<Set<DovahLinkStateArea>> first = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests.single.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        await first;

        final Future<Set<DovahLinkStateArea>> repeated = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        expect(requestService.requests.last.payload, <String, dynamic>{
          'stateAreas': <String>['character_xp'],
        });
        requestService.requests.last.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        expect(await repeated, isEmpty);
        expect(service.desiredStateAreas, <DovahLinkStateArea>{
          DovahLinkStateArea.characterXp,
        });

        final Future<Set<DovahLinkStateArea>> firstRemoval = service
            .unsubscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests.last.reply.complete(
          _acknowledgement(accepted: const <String>[]),
        );
        await firstRemoval;
        final Future<Set<DovahLinkStateArea>> repeatedRemoval = service
            .unsubscribeStateArea(DovahLinkStateArea.characterXp);
        expect(requestService.requests.last.payload, <String, dynamic>{
          'stateAreas': <String>[],
        });
        requestService.requests.last.reply.complete(
          _acknowledgement(accepted: const <String>[]),
        );
        expect(await repeatedRemoval, isEmpty);
        expect(service.desiredStateAreas, isEmpty);
      },
    );

    test(
      'Method synchronizeDesiredStateAreas ignores an old subscribe ack after unsubscribe',
      () async {
        final Future<Set<DovahLinkStateArea>> first = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        final Future<Set<DovahLinkStateArea>> second = service
            .unsubscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests[1].reply.complete(
          _acknowledgement(accepted: const <String>[]),
        );
        await second;
        requestService.requests[0].reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        await first;

        expect(stateMessageHandler.subscribedStateAreas, isEmpty);
        expect(service.desiredStateAreas, isEmpty);
      },
    );

    test(
      'Method onSessionEnded clears accepted areas but preserves desired intent',
      () async {
        final Future<Set<DovahLinkStateArea>> update = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests.single.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        await update;

        service.onSessionEnded();

        expect(stateMessageHandler.subscribedStateAreas, isEmpty);
        expect(service.desiredStateAreas, <DovahLinkStateArea>{
          DovahLinkStateArea.characterXp,
        });
      },
    );

    test(
      'Method onSessionEnded ignores a late acknowledgement from the ended session',
      () async {
        final Future<Set<DovahLinkStateArea>> update = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        service.onSessionEnded();
        requestService.requests.single.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );

        expect(await update, isEmpty);
        expect(stateMessageHandler.subscribedStateAreas, isEmpty);
        expect(service.desiredStateAreas, <DovahLinkStateArea>{
          DovahLinkStateArea.characterXp,
        });
      },
    );

    test(
      'Method restoreDesiredStateAreas resends only the remembered set',
      () async {
        final Future<Set<DovahLinkStateArea>> update = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests.single.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        await update;
        service.onSessionEnded();

        service.restoreDesiredStateAreas();

        expect(requestService.requests, hasLength(2));
        expect(requestService.requests.last.payload, <String, dynamic>{
          'stateAreas': <String>['character_xp'],
        });
        requestService.requests.last.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        await pumpEventQueue();
        expect(stateMessageHandler.subscribedStateAreas, <String>{
          'character_xp',
        });
      },
    );

    test(
      'Method restoreDesiredStateAreas contains a failed best-effort request',
      () async {
        final Future<Set<DovahLinkStateArea>> update = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests.single.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        await update;
        service.onSessionEnded();

        service.restoreDesiredStateAreas();
        requestService.requests.last.reply.completeError(
          const DovahLinkConnectionException('Connection lost.'),
        );
        await pumpEventQueue();

        expect(stateMessageHandler.subscribedStateAreas, isEmpty);
        expect(service.desiredStateAreas, <DovahLinkStateArea>{
          DovahLinkStateArea.characterXp,
        });
      },
    );

    test(
      'Method clearDesiredStateAreas removes intent and the accepted gate',
      () async {
        final Future<Set<DovahLinkStateArea>> update = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests.single.reply.complete(
          _acknowledgement(accepted: <String>['character_xp']),
        );
        await update;

        service.clearDesiredStateAreas();

        expect(service.desiredStateAreas, isEmpty);
        expect(stateMessageHandler.subscribedStateAreas, isEmpty);
        service.restoreDesiredStateAreas();
        expect(requestService.requests, hasLength(1));
      },
    );

    test(
      'Method restoreDesiredStateAreas is a no-op when no intent remains',
      () {
        service.restoreDesiredStateAreas();

        expect(requestService.requests, isEmpty);
      },
    );

    test(
      'Method synchronizeDesiredStateAreas fails closed on malformed acknowledgements',
      () async {
        final List<({List<String> accepted, List<String> rejected})> malformed =
            <({List<String> accepted, List<String> rejected})>[
              (accepted: <String>['unknown_area'], rejected: <String>[]),
              (
                accepted: <String>['character_xp', 'character_xp'],
                rejected: <String>[],
              ),
              (
                accepted: <String>['character_xp'],
                rejected: <String>['character_xp'],
              ),
              (accepted: <String>[], rejected: <String>[]),
              (
                accepted: <String>['character_xp', 'character_health'],
                rejected: <String>[],
              ),
            ];

        for (final ({List<String> accepted, List<String> rejected}) payload
            in malformed) {
          final Future<Set<DovahLinkStateArea>> update = service
              .subscribeStateArea(DovahLinkStateArea.characterXp);
          requestService.requests.last.reply.complete(
            _acknowledgement(
              accepted: payload.accepted,
              rejected: payload.rejected,
            ),
          );

          await expectLater(update, throwsA(isA<DovahLinkProtocolException>()));
        }

        final Future<Set<DovahLinkStateArea>> malformedPayload = service
            .subscribeStateArea(DovahLinkStateArea.characterXp);
        requestService.requests.last.reply.complete(
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.subscriptionAck,
            payload: const <String, dynamic>{},
          ),
        );
        await expectLater(
          malformedPayload,
          throwsA(isA<DovahLinkProtocolException>()),
        );

        verify(
          () => sessionService.onProtocolViolation(
            any(that: isA<DovahLinkProtocolException>()),
            orphanRetrySafeOperations: false,
          ),
        ).called(malformed.length + 1);
        expect(stateMessageHandler.subscribedStateAreas, isEmpty);
      },
    );
  });
}
