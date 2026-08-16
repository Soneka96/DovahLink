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
