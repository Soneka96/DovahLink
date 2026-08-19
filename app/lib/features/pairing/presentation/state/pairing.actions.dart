import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

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
    this.credentialRejectedMessage,
  });

  /// The DovahLink Bridge/mod release version reported by `hello_ack`.
  final String bridgeVersion;

  /// Whether this session already holds a trusted credential.
  final bool trusted;

  /// A user-safe explanation, or `null` when not applicable. Set only when this authentication
  /// recovered from a rejected `trusted_device_credential` hello (revoked or unrecognized) by
  /// discarding the stale credential and re-authenticating as unpaired.
  final String? credentialRejectedMessage;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    bridgeVersion,
    trusted,
    credentialRejectedMessage,
  ];
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
  const PairingCodeAvailableAction({this.expiresInSeconds});

  /// The active code's remaining validity in seconds, or null when the
  /// bridge did not report one.
  final int? expiresInSeconds;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [expiresInSeconds];
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

/// Marks the bridge as unreachable. Distinct from [PairingFailedAction]: this
/// is an expected transport-level condition (the bridge may not be running
/// yet), not a rejected pairing attempt.
class PairingDisconnectedAction extends Equatable {
  /// Creates a disconnected-state action.
  const PairingDisconnectedAction();

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

/// Requests leaving the pairing screen and returning to home.
class PairingBackRequestedAction extends Equatable {
  /// Creates a back-navigation request.
  const PairingBackRequestedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Closes the pairing session and resets its state.
class PairingDisposedAction extends Equatable {
  /// Creates a disposed-state action. [wasTrusted] is captured at dispatch time, before the
  /// reducer resets [PairingState] -- middleware always sees reduced state, so this is the only
  /// way its handler can know whether pairing had already succeeded.
  const PairingDisposedAction({required this.wasTrusted});

  /// Whether the pairing phase was [PairingPhase.trusted] at dispose time. When `true`, the
  /// established trust and connection are kept rather than disconnected.
  final bool wasTrusted;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [wasTrusted];
}

/// Requests redisplay of the active pairing code.
class PairingRenotifyRequestedAction extends Equatable {
  /// Creates a renotify-request action.
  const PairingRenotifyRequestedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Marks the active pairing code as redisplayed in Skyrim.
class PairingRenotifySucceededAction extends Equatable {
  /// Creates a renotify-success action.
  const PairingRenotifySucceededAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Carries renotify cooldown information when redisplay was rejected.
class PairingRenotifyCooldownAction extends Equatable {
  /// Creates a renotify-cooldown action.
  const PairingRenotifyCooldownAction({required this.retryAfterSeconds});

  /// The remaining cooldown in seconds before the next manual renotify.
  final int retryAfterSeconds;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [retryAfterSeconds];
}

/// Requests cancellation of the active pairing challenge or pending credential.
class PairingCancelRequestedAction extends Equatable {
  /// Creates a cancel-request action.
  const PairingCancelRequestedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Marks the active pairing challenge or pending credential as cancelled.
class PairingCancelSucceededAction extends Equatable {
  /// Creates a cancel-success action.
  const PairingCancelSucceededAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Marks a wrong pairing code entry when attempts remain before hard limit.
class PairingConfirmFailedWithAttemptsRemainingAction extends Equatable {
  /// Creates a failed-but-retriable code-submission action.
  const PairingConfirmFailedWithAttemptsRemainingAction({
    required this.message,
  });

  /// A user-safe explanation of why the code was rejected.
  final String message;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [message];
}
