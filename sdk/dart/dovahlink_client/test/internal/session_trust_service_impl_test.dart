import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/session_state.dart';
import 'package:dovahlink_client_sdk/src/internal/session_trust_service_impl.dart';

/// Mock session state used to isolate [SessionTrustServiceImpl]'s own behavior, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockSessionState extends Mock implements SessionState {}

/// Runs session-trust-service behavior tests.
void main() {
  late MockSessionState state;
  late SessionTrustServiceImpl service;

  setUp(() {
    state = MockSessionState();
    service = SessionTrustServiceImpl(state: state);
  });

  group('Method markTrusted behaves correctly', () {
    test('Method markTrusted forwards to SessionState.markTrusted', () {
      when(() => state.markTrusted()).thenAnswer((_) {});

      service.markTrusted();

      verify(() => state.markTrusted()).called(1);
    });
  });
}
