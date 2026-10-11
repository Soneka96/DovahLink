import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../readiness/mock_gameplay_readiness_service.dart';
import '../state/mock_subscription_service.dart';

/// Mock session state used to isolate [SessionAdmissionService]'s own behavior, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockSessionState extends Mock implements SessionState {}

/// Mock request service -- its own orphaned-retry logic is
/// `request_service_test.dart`'s responsibility; this file only proves
/// [SessionAdmissionService] calls it after admitting.
class MockRequestService extends Mock implements IRequestService {}

/// Builds a representative connected Host for admission tests.
DovahLinkHost _currentHost() => DovahLinkHost(
  hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
  hostName: 'LOCAL-HOST',
  endpoint: Uri.parse('ws://127.0.0.1:58231/'),
);

/// Runs session-admission-service behavior tests.
void main() {
  late MockSessionState state;
  late MockRequestService requestService;
  late MockSubscriptionService subscriptionService;
  late MockGameplayReadinessService gameplayReadinessService;
  late SessionAdmissionService service;

  setUpAll(() {
    registerFallbackValue(DovahLinkTrustState.unpaired);
    registerFallbackValue(
      DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'LOCAL-HOST',
        endpoint: Uri.parse('ws://127.0.0.1:58231/'),
      ),
    );
  });

  setUp(() {
    state = MockSessionState();
    requestService = MockRequestService();
    subscriptionService = MockSubscriptionService();
    gameplayReadinessService = MockGameplayReadinessService();
    when(
      () => gameplayReadinessService.onSessionAdmitted(
        sessionId: any(named: 'sessionId'),
        stateAuthorityId: any(named: 'stateAuthorityId'),
      ),
    ).thenAnswer((_) {});
    when(
      () => state.admit(
        sessionId: any(named: 'sessionId'),
        trustState: any(named: 'trustState'),
        currentHost: any(named: 'currentHost'),
      ),
    ).thenAnswer((_) {});
    when(() => requestService.retryOrphanedOperations()).thenAnswer((_) {});
    when(
      () => subscriptionService.restoreDesiredStateAreas(),
    ).thenAnswer((_) {});
    service = SessionAdmissionService(
      state: state,
      requestService: requestService,
      subscriptionService: subscriptionService,
      gameplayReadinessService: gameplayReadinessService,
    );
  });

  group('Method admitSession behaves correctly', () {
    test(
      'Method admitSession decides readiness for an unpaired session as well as a trusted one',
      () {
        service.admitSession(
          sessionId: 'session-unpaired',
          trustState: DovahLinkTrustState.unpaired,
          currentHost: _currentHost(),
          stateAuthorityId: 'state-authority-9',
        );

        verify(
          () => gameplayReadinessService.onSessionAdmitted(
            sessionId: 'session-unpaired',
            stateAuthorityId: 'state-authority-9',
          ),
        ).called(1);
      },
    );

    test(
      'Method admitSession records sessionId and trustState onto SessionState',
      () {
        service.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
          stateAuthorityId: 'state-authority-1',
        );

        verify(
          () => state.admit(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.trusted,
            currentHost: _currentHost(),
          ),
        ).called(1);
      },
    );

    test(
      'Method admitSession retries orphaned operations after recording the new session',
      () {
        service.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
          stateAuthorityId: 'state-authority-1',
        );

        verifyInOrder(<void Function()>[
          () => state.admit(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.trusted,
            currentHost: _currentHost(),
          ),
          () => gameplayReadinessService.onSessionAdmitted(
            sessionId: 'session-1',
            stateAuthorityId: 'state-authority-1',
          ),
          () => requestService.retryOrphanedOperations(),
          () => subscriptionService.restoreDesiredStateAreas(),
        ]);
      },
    );

    test(
      'Method admitSession restores desired areas only for a trusted session',
      () {
        service.admitSession(
          sessionId: 'session-unpaired',
          trustState: DovahLinkTrustState.unpaired,
          currentHost: _currentHost(),
          stateAuthorityId: 'state-authority-1',
        );
        verifyNever(() => subscriptionService.restoreDesiredStateAreas());

        service.admitSession(
          sessionId: 'session-trusted',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
          stateAuthorityId: 'state-authority-1',
        );
        verify(() => subscriptionService.restoreDesiredStateAreas()).called(1);
      },
    );
  });
}
