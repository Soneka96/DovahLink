import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/state/character_state_module.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_recovery_composition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_recovery_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import '../../fixtures/fixtures.dart';
import 'controlled_request_service.dart';
import 'mock_session_service.dart';
import 'mock_state_revision_tracker.dart';

/// Mock registered definition used to declare each area's capability.
class MockStateDomainDefinition extends Mock
    implements IStateDomainDefinition<Object?> {}

/// Builds one registered definition whose tracker is already stale.
/// @param stateArea The synthetic area name.
/// @param supportsEvents Whether the definition declares Event support.
/// @return The definition and its tracker, so a test can observe the tracker's subscription.
({MockStateDomainDefinition domain, MockStateRevisionTracker<Object?> tracker})
buildStaleDefinition({
  required String stateArea,
  required bool supportsEvents,
}) {
  final MockStateRevisionTracker<Object?> tracker =
      MockStateRevisionTracker<Object?>();
  when(() => tracker.current).thenReturn(
    Fixtures.buildStateSynchronization<Object?>(
      status: DovahLinkStateStatus.stale,
      stateAuthorityId: 'authority-1',
      playContextId: 'context-1',
      revision: 1,
    ),
  );
  when(
    () => tracker.changes,
  ).thenAnswer((_) => const Stream<StateSynchronization<Object?>>.empty());
  when(() => tracker.recoveryBufferOverflowed).thenReturn(false);
  final MockStateDomainDefinition domain = MockStateDomainDefinition();
  when(() => domain.stateArea).thenReturn(stateArea);
  when(() => domain.supportsEvents).thenReturn(supportsEvents);
  when(() => domain.tracker).thenReturn(tracker);
  return (domain: domain, tracker: tracker);
}

/// Runs registered-definition recovery composition tests.
void main() {
  late ControlledRequestService requests;
  late MockSessionService session;

  setUp(() {
    requests = ControlledRequestService();
    session = MockSessionService();
    when(
      () => session.currentTrustState,
    ).thenReturn(DovahLinkTrustState.trusted);
    when(
      () => session.connectionState,
    ).thenReturn(DovahLinkConnectionState.connected);
  });

  group('Function startStateRecovery behaves correctly', () {
    test(
      'Function startStateRecovery starts one service for an Event-capable definition',
      () async {
        final ({
          MockStateDomainDefinition domain,
          MockStateRevisionTracker<Object?> tracker,
        })
        eventDomain = buildStaleDefinition(
          stateArea: 'synthetic_event_area',
          supportsEvents: true,
        );

        final List<IStateRecoveryService<Object?>> services =
            startStateRecovery(
              domains: <MockStateDomainDefinition>[eventDomain.domain],
              requestService: requests,
              sessionService: session,
            );
        await Future<void>.delayed(Duration.zero);

        expect(services, hasLength(1));
        expect(requests.requests, hasLength(1));
        expect(
          requests.requests.single.messageType,
          ProtocolMessageType.snapshotRequest,
        );
        expect(requests.requests.single.payload, <String, dynamic>{
          'stateArea': 'synthetic_event_area',
          'knownRevision': 1,
        });
        verify(() => eventDomain.tracker.beginRecovery()).called(1);
      },
    );

    test(
      'Function startStateRecovery gives a Snapshot-only definition no service',
      () async {
        final ({
          MockStateDomainDefinition domain,
          MockStateRevisionTracker<Object?> tracker,
        })
        snapshotOnly = buildStaleDefinition(
          stateArea: 'synthetic_snapshot_area',
          supportsEvents: false,
        );

        final List<IStateRecoveryService<Object?>> services =
            startStateRecovery(
              domains: <MockStateDomainDefinition>[snapshotOnly.domain],
              requestService: requests,
              sessionService: session,
            );
        await Future<void>.delayed(Duration.zero);

        expect(services, isEmpty);
        expect(requests.requests, isEmpty);
        verifyNever(() => snapshotOnly.tracker.changes);
        verifyNever(() => snapshotOnly.tracker.beginRecovery());
      },
    );

    test(
      'Function startStateRecovery composes one service per Event-capable definition in a mixed list',
      () async {
        final ({
          MockStateDomainDefinition domain,
          MockStateRevisionTracker<Object?> tracker,
        })
        firstEvent = buildStaleDefinition(
          stateArea: 'synthetic_first_event',
          supportsEvents: true,
        );
        final ({
          MockStateDomainDefinition domain,
          MockStateRevisionTracker<Object?> tracker,
        })
        snapshotOnly = buildStaleDefinition(
          stateArea: 'synthetic_snapshot_area',
          supportsEvents: false,
        );
        final ({
          MockStateDomainDefinition domain,
          MockStateRevisionTracker<Object?> tracker,
        })
        secondEvent = buildStaleDefinition(
          stateArea: 'synthetic_second_event',
          supportsEvents: true,
        );

        final List<IStateRecoveryService<Object?>> services =
            startStateRecovery(
              domains: <MockStateDomainDefinition>[
                firstEvent.domain,
                snapshotOnly.domain,
                secondEvent.domain,
              ],
              requestService: requests,
              sessionService: session,
            );
        await Future<void>.delayed(Duration.zero);

        expect(services, hasLength(2));
        expect(requests.requests, hasLength(2));
        expect(
          requests.requests[0].payload['stateArea'],
          'synthetic_first_event',
        );
        expect(
          requests.requests[1].payload['stateArea'],
          'synthetic_second_event',
        );
        verify(() => firstEvent.tracker.beginRecovery()).called(1);
        verify(() => secondEvent.tracker.beginRecovery()).called(1);
        verifyNever(() => snapshotOnly.tracker.changes);
        verifyNever(() => snapshotOnly.tracker.beginRecovery());
      },
    );

    test(
      'Function startStateRecovery composes only Level recovery for the real Character definitions',
      () {
        final ICharacterStateModule character = CharacterStateModule();

        final List<IStateRecoveryService<Object?>> services =
            startStateRecovery(
              domains: character.domains,
              requestService: requests,
              sessionService: session,
            );

        expect(services, hasLength(1));
        expect(requests.requests, isEmpty);
      },
    );

    test('Function startStateRecovery is a no-op for no definitions', () {
      final List<IStateRecoveryService<Object?>> services = startStateRecovery(
        domains: const <IStateDomainDefinition<Object?>>[],
        requestService: requests,
        sessionService: session,
      );

      expect(services, isEmpty);
      expect(requests.requests, isEmpty);
    });
  });
}
