import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter_impl.dart';
import 'package:dovahlink_client_sdk/src/internal/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Mock session service used to isolate [ConnectionLifecycleReporterImpl]'s forwarding behavior.
class MockSessionService extends Mock implements SessionService {}

/// Runs connection-lifecycle-reporter-view behavior tests.
void main() {
  late MockSessionService sessionService;
  late ConnectionLifecycleReporterImpl reporter;

  setUp(() {
    sessionService = MockSessionService();
    reporter = ConnectionLifecycleReporterImpl(sessionService);
  });

  group('Method onUnhealthy behaves correctly', () {
    test('Method onUnhealthy forwards to SessionService.onUnhealthy', () {
      const DovahLinkConnectionException reason = DovahLinkConnectionException(
        'timed out',
      );

      reporter.onUnhealthy(reason);

      verify(() => sessionService.onUnhealthy(reason)).called(1);
    });
  });

  group('Method onProtocolViolation behaves correctly', () {
    test(
      'Method onProtocolViolation forwards to SessionService.onProtocolViolation',
      () {
        const DovahLinkConnectionException reason =
            DovahLinkConnectionException('bad frame');

        reporter.onProtocolViolation(reason, orphanRetrySafeOperations: true);

        verify(
          () => sessionService.onProtocolViolation(
            reason,
            orphanRetrySafeOperations: true,
          ),
        ).called(1);
      },
    );
  });

  group('Method onSessionInvalidated behaves correctly', () {
    test(
      'Method onSessionInvalidated forwards to SessionService.onSessionInvalidated',
      () {
        reporter.onSessionInvalidated(AdministrativeInvalidationReason.revoked);

        verify(
          () => sessionService.onSessionInvalidated(
            AdministrativeInvalidationReason.revoked,
          ),
        ).called(1);
      },
    );
  });

  group('Method onUnsolicitedError behaves correctly', () {
    test(
      'Method onUnsolicitedError forwards to SessionService.onUnsolicitedError',
      () {
        const ErrorPayload error = ErrorPayload(
          code: ProtocolErrorCode.rateLimited,
          message: 'slow down',
          retryable: true,
        );

        reporter.onUnsolicitedError(error);

        verify(() => sessionService.onUnsolicitedError(error)).called(1);
      },
    );
  });
}
