import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_registry.dart';

/// Implements [PendingOperationRegistry] by delegating to [PendingOperationBookkeeping] -- so
/// `RequestServiceImpl`'s still-unchanged owned collaborator `PendingOperationTransmitter` (built
/// against the pre-decomposition `PendingOperationRegistry` port) can be handed a real, distinct
/// object under its own true identity, rather than `RequestServiceImpl` implementing this port
/// itself and passing itself under a second name. Eliminated, along with
/// `PendingOperationRegistry`, at the composition-root cutover.
class PendingOperationRegistryImpl implements PendingOperationRegistry {
  /// The bookkeeping this registry registers into.
  final PendingOperationBookkeeping _bookkeeping;

  /// Creates a pending-operation-registry view over [bookkeeping].
  PendingOperationRegistryImpl(this._bookkeeping);

  /// Implements [PendingOperationRegistry.register].
  @override
  void register(String messageId, PendingOperation operation) =>
      _bookkeeping.register(messageId, operation);
}
