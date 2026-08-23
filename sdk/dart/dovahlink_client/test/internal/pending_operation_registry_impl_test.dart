import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_registry_impl.dart';
import '../fixtures/internal/pending_operation.fixture.dart';

/// Mock pending-operation bookkeeping used to isolate [PendingOperationRegistryImpl]'s forwarding
/// behavior, per `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockPendingOperationBookkeeping extends Mock
    implements PendingOperationBookkeeping {}

/// Runs pending-operation-registry-view behavior tests.
void main() {
  late MockPendingOperationBookkeeping bookkeeping;
  late PendingOperationRegistryImpl registry;

  setUpAll(() {
    registerFallbackValue(buildPendingOperation());
  });

  setUp(() {
    bookkeeping = MockPendingOperationBookkeeping();
    registry = PendingOperationRegistryImpl(bookkeeping);
  });

  group('Method register behaves correctly', () {
    test(
      'Method register forwards to PendingOperationBookkeeping.register',
      () {
        final PendingOperation operation = buildPendingOperation();

        registry.register('id-1', operation);

        verify(() => bookkeeping.register('id-1', operation)).called(1);
      },
    );
  });
}
