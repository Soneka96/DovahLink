import 'package:equatable/equatable.dart';

/// Requests a new pairing session: connect, authenticate, and recover any
/// interrupted pairing confirmation.
class PairingStartedAction extends Equatable {
  /// Creates a pairing-start request.
  const PairingStartedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Carries the result of authenticating a bridge session.
class PairingAuthenticatedAction extends Equatable {
  /// Creates an authenticated-state action.
  const PairingAuthenticatedAction({
    required this.bridgeVersion,
    required this.trusted,
  });

  /// The DovahLink Bridge/mod release version reported by `hello_ack`.
  final String bridgeVersion;

  /// Whether this session already holds a trusted credential.
  final bool trusted;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [bridgeVersion, trusted];
}

/// Requests a pairing challenge.
class PairingCodeRequestedAction extends Equatable {
  /// Creates a code-request action.
  const PairingCodeRequestedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Marks a pairing code as available to enter.
class PairingCodeAvailableAction extends Equatable {
  /// Creates a code-available action.
  const PairingCodeAvailableAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Submits the user-entered pairing code.
class PairingCodeSubmittedAction extends Equatable {
  /// Creates a code-submission action.
  const PairingCodeSubmittedAction({required this.code, this.displayName});

  /// The six-digit code the user read from Skyrim.
  final String code;

  /// An optional, presentation-only label for the resulting trusted client.
  final String? displayName;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [code, displayName];
}

/// Marks pairing as complete and trusted.
class PairingConfirmedAction extends Equatable {
  /// Creates a confirmed-state action.
  const PairingConfirmedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Carries a user-safe pairing failure message.
class PairingFailedAction extends Equatable {
  /// Creates a failed-state action.
  const PairingFailedAction(this.message);

  /// A user-safe explanation of why pairing failed.
  final String message;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [message];
}

/// Closes the pairing session and resets its state.
class PairingDisposedAction extends Equatable {
  /// Creates a disposed-state action.
  const PairingDisposedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}
