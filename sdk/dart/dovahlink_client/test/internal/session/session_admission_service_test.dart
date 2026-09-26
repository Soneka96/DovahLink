import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
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
    );
  });

  group('Method admitSession behaves correctly', () {
    test(
      'Method admitSession records sessionId and trustState onto SessionState',
      () {
        service.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
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
        );

        verifyInOrder(<void Function()>[
          () => state.admit(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.trusted,
            currentHost: _currentHost(),
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
        );
        verifyNever(() => subscriptionService.restoreDesiredStateAreas());

        service.admitSession(
          sessionId: 'session-trusted',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );
        verify(() => subscriptionService.restoreDesiredStateAreas()).called(1);
      },
    );
  });
}
