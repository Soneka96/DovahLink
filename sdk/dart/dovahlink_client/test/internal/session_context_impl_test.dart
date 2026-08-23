import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/session_context_impl.dart';
import 'package:dovahlink_client_sdk/src/internal/session_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Mock session service used to isolate [SessionContextImpl]'s forwarding behavior.
class MockSessionService extends Mock implements SessionService {}

/// Runs session-context-view behavior tests.
void main() {
  late MockSessionService sessionService;
  late SessionContextImpl context;

  setUp(() {
    sessionService = MockSessionService();
    context = SessionContextImpl(sessionService);
  });

  group('Property currentSessionId behaves correctly', () {
    test(
      'Property currentSessionId forwards to SessionService.currentSessionId',
      () {
        when(() => sessionService.currentSessionId).thenReturn('session-1');

        expect(context.currentSessionId, 'session-1');
      },
    );
  });

  group('Property currentTrustState behaves correctly', () {
    test(
      'Property currentTrustState forwards to SessionService.currentTrustState',
      () {
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);

        expect(context.currentTrustState, DovahLinkTrustState.trusted);
      },
    );
  });
}
