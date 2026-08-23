import 'package:dovahlink_client_sdk/src/internal/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/reply_resolver.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';

/// Implements [ReplyResolver] by delegating to [PendingOperationBookkeeping] -- so
/// `RequestServiceImpl`'s still-unchanged owned collaborator `MessageRouter` (built against the
/// pre-decomposition `ReplyResolver` port) can be handed a real, distinct object under its own
/// true identity, rather than `RequestServiceImpl` implementing this port itself and passing
/// itself under a second name. Eliminated, along with `ReplyResolver`, at the composition-root
/// cutover.
class ReplyResolverImpl implements ReplyResolver {
  /// The bookkeeping this resolver reads and mutates.
  final PendingOperationBookkeeping _bookkeeping;

  /// Creates a reply-resolver view over [bookkeeping].
  ReplyResolverImpl(this._bookkeeping);

  /// Implements [ReplyResolver.resolveReply].
  @override
  bool resolveReply(String correlationId, Envelope envelope) =>
      _bookkeeping.resolveReply(correlationId, envelope);
}
