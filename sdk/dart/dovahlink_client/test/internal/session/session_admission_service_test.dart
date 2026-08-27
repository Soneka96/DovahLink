import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Mock session state used to isolate [SessionAdmissionService]'s own behavior, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockSessionState extends Mock implements SessionState {}

/// Mock request service -- its own orphaned-retry logic is
/// `request_service_impl_test.dart`'s responsibility; this file only proves
/// [SessionAdmissionService] calls it after admitting.
class MockRequestService extends Mock implements RequestService {}

/// Runs session-admission-service behavior tests.
void main() {
  late MockSessionState state;
  late MockRequestService requestService;
  late SessionAdmissionService service;

  setUpAll(() {
    registerFallbackValue(DovahLinkTrustState.unpaired);
  });

  setUp(() {
    state = MockSessionState();
    requestService = MockRequestService();
    when(
      () => state.admit(
        sessionId: any(named: 'sessionId'),
        trustState: any(named: 'trustState'),
      ),
    ).thenAnswer((_) {});
    when(() => requestService.retryOrphanedOperations()).thenAnswer((_) {});
    service = SessionAdmissionService(
      state: state,
      requestService: requestService,
    );
  });

  group('Method admitSession behaves correctly', () {
    test(
      'Method admitSession records sessionId and trustState onto SessionState',
      () {
        service.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );

        verify(
          () => state.admit(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.trusted,
          ),
        ).called(1);
      },
    );

    test(
      'Method admitSession retries orphaned operations after recording the new session',
      () {
        service.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );

        verifyInOrder(<void Function()>[
          () => state.admit(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.unpaired,
          ),
          () => requestService.retryOrphanedOperations(),
        ]);
      },
    );
  });
}
