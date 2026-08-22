import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The three independent properties [DovahLinkClient] classifies every request/operation against,
/// per `ai/context/sdk/api-design.md`'s "Request retry safety, session requirement, and timeout
/// class". Bundled as one value rather than three parallel fields, since they are read and
/// enforced together for a single pending operation.
class RequestPolicy {
  /// Creates a request policy.
  const RequestPolicy({
    required this.retrySafe,
    required this.requiredTrustState,
    required this.timeoutClass,
  });

  /// Whether repetition of this request cannot produce an incorrect duplicate effect. A
  /// `retrySafe` operation may be retried once, automatically, after a reconnect re-establishes a
  /// session that still satisfies [requiredTrustState]; a repeated failure of that retry is
  /// surfaced rather than retried again. A non-`retrySafe` operation whose response is lost to an
  /// ambiguous transport failure is never automatically re-sent -- the SDK cannot know whether the
  /// Bridge already executed it.
  final bool retrySafe;

  /// The trust state the current session must already be at before this request may be sent, or
  /// `null` for `hello` alone -- the one bootstrap request that establishes the session and
  /// therefore requires only a live transport connection, no existing trust state. Every other
  /// Phase 3.3 operation requires [DovahLinkTrustState.unpaired] (a session must already exist).
  /// A queued retry-safe operation that survives a reconnect is revalidated against this value
  /// before it is retransmitted.
  final DovahLinkTrustState? requiredTrustState;

  /// The centralized bounded timeout category this request is classified against; see
  /// `shared/constants.dart`'s `kTimeoutClassDurations` for the actual duration.
  final TimeoutClass timeoutClass;
}
