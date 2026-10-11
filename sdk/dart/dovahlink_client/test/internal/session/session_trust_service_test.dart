import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_trust_service.dart';
import '../state/mock_subscription_service.dart';

/// Mock session state used to isolate [SessionTrustService]'s own behavior, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockSessionState extends Mock implements SessionState {}

/// Runs session-trust-service behavior tests.
void main() {
  late MockSessionState state;
  late MockSubscriptionService subscriptionService;
  late SessionTrustService service;

  setUp(() {
    state = MockSessionState();
    subscriptionService = MockSubscriptionService();
    when(() => state.markTrusted()).thenAnswer((_) {});
    when(() => state.sessionId).thenReturn('session-1');
    when(
      () => subscriptionService.restoreDesiredStateAreas(),
    ).thenAnswer((_) {});
    service = SessionTrustService(
      state: state,
      subscriptionService: subscriptionService,
    );
  });

  group('Method markTrusted behaves correctly', () {
    test('Method markTrusted forwards to SessionState.markTrusted', () {
      service.markTrusted();

      verify(() => state.markTrusted()).called(1);
    });

    test(
      'Method markTrusted restores subscriptions after upgrading an admitted session',
      () {
        final List<String> calls = <String>[];
        when(() => state.markTrusted()).thenAnswer((_) => calls.add('trust'));
        when(
          () => subscriptionService.restoreDesiredStateAreas(),
        ).thenAnswer((_) => calls.add('restore'));

        service.markTrusted();

        expect(calls, <String>['trust', 'restore']);
      },
    );

    test('Method markTrusted does not restore without an admitted session', () {
      when(() => state.sessionId).thenReturn(null);

      service.markTrusted();

      verify(() => state.markTrusted()).called(1);
      verifyNever(() => subscriptionService.restoreDesiredStateAreas());
    });
  });
}
