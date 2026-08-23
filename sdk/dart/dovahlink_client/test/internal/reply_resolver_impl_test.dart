import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/reply_resolver_impl.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import '../fixtures/protocol/envelope.fixture.dart';

/// Mock pending-operation bookkeeping used to isolate [ReplyResolverImpl]'s forwarding behavior,
/// per `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockPendingOperationBookkeeping extends Mock
    implements PendingOperationBookkeeping {}

/// Runs reply-resolver-view behavior tests.
void main() {
  late MockPendingOperationBookkeeping bookkeeping;
  late ReplyResolverImpl resolver;

  setUpAll(() {
    registerFallbackValue(buildEnvelope());
  });

  setUp(() {
    bookkeeping = MockPendingOperationBookkeeping();
    resolver = ReplyResolverImpl(bookkeeping);
  });

  group('Method resolveReply behaves correctly', () {
    test(
      'Method resolveReply forwards to PendingOperationBookkeeping.resolveReply and returns its result',
      () {
        final Envelope reply = buildEnvelope(correlationId: 'id-1');
        when(() => bookkeeping.resolveReply('id-1', reply)).thenReturn(true);

        final bool resolved = resolver.resolveReply('id-1', reply);

        expect(resolved, isTrue);
        verify(() => bookkeeping.resolveReply('id-1', reply)).called(1);
      },
    );

    test(
      'Method resolveReply returns false when PendingOperationBookkeeping reports no match',
      () {
        final Envelope reply = buildEnvelope(correlationId: 'no-such-id');
        when(
          () => bookkeeping.resolveReply('no-such-id', reply),
        ).thenReturn(false);

        expect(resolver.resolveReply('no-such-id', reply), isFalse);
      },
    );
  });
}
