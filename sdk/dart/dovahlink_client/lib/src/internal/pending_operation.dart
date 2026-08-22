import 'dart:async';

import '../protocol/envelope.dart';
import '../protocol/json_map.dart';
import '../request_policy.dart';

/// One request awaiting its correlated reply -- or, if `policy.retrySafe` and the connection was
/// lost before a reply arrived, awaiting a chance to be retransmitted once after the next
/// successful `hello`. Mutable bookkeeping for exactly one logical request across however many
/// wire attempts (the initial send, plus at most one retry) it takes to resolve [completer].
class PendingOperation {
  /// Creates a pending operation for one logical request.
  PendingOperation({
    required this.messageType,
    required this.payload,
    required this.policy,
  });

  /// The outgoing message type this operation sends.
  final String messageType;

  /// The outgoing payload this operation sends.
  final JsonMap payload;

  /// This operation's classification against the retry-safety/session-requirement/timeout-class
  /// model.
  final RequestPolicy policy;

  /// Resolved once, either by a correlated reply on the wire or by an outright failure -- shared
  /// across the initial attempt and its at-most-one retry, so the original caller's `await`
  /// resolves transparently regardless of which attempt actually succeeds.
  final Completer<Envelope> completer = Completer<Envelope>();

  /// Whether this operation has already been retransmitted once after a reconnect -- caps retry
  /// at exactly one attempt, per [RequestPolicy.retrySafe]'s contract.
  bool hasRetried = false;

  /// Bounds the current wire attempt; cancelled once [completer] settles or a new attempt starts.
  Timer? timer;
}
