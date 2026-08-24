import 'package:equatable/equatable.dart';

/// Base class for expected runtime failures.
abstract class Failure extends Equatable {
  /// Creates a failure with a user-safe [message].
  const Failure(this.message);

  /// A user-safe description of the failure.
  final String message;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [message];
}

/// Indicates that input or received data was invalid.
class ValidationFailure extends Failure {
  /// Creates a validation failure.
  const ValidationFailure(super.message);
}

/// Indicates that a connection or remote operation failed.
class NetworkFailure extends Failure {
  /// Creates a network failure.
  const NetworkFailure(super.message);
}

/// Indicates that persisted client data could not be read or written.
class DatabaseFailure extends Failure {
  /// Creates a database failure.
  const DatabaseFailure(super.message);
}

/// Indicates that the bridge rejected a pairing attempt (an expired,
/// invalid, or rate-limited code), as distinct from a transport-level
/// [NetworkFailure].
class PairingFailure extends Failure {
  /// Creates a pairing failure.
  const PairingFailure(super.message);
}

/// A [PairingFailure] the user can retry against the same still-active
/// challenge: a wrong code that hasn't hit the hard attempt limit yet, or an
/// attempt rejected only for being paced too soon. Distinct from a plain
/// [PairingFailure] so presentation code can keep the user on the code-entry
/// screen instead of bouncing them out of the pairing flow.
class PairingRetriableFailure extends PairingFailure {
  /// Creates a retriable pairing failure.
  const PairingRetriableFailure(super.message);
}

/// Indicates the bridge administratively ended this device's session
/// (revoked, blocked, trust reset, or factory reset), as distinct from an
/// ordinary [NetworkFailure]. A consumer must not treat this as transient
/// transport loss eligible for automatic retry; recovery is always an
/// explicit user action.
class SessionInvalidatedFailure extends Failure {
  /// The one canonical, reason-agnostic wording used for every administrative invalidation
  /// (revoked, blocked, trust reset, or factory reset), per PLAN.md's Stage 3.
  static const SessionInvalidatedFailure administrative =
      SessionInvalidatedFailure(
        'This device was disconnected by the bridge. Try again.',
      );

  /// Creates a session-invalidated failure.
  const SessionInvalidatedFailure(super.message);
}
