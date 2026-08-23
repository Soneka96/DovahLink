import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The SDK-owned persisted client identity, credential, and pairing recovery state. Versioned so a
/// [ClientStorage] implementation can detect and reject a format it does not understand rather
/// than silently misinterpreting it. See `ai/context/sdk/persistence.md`.
class PersistedClientState {
  /// The current persisted-state format version this SDK writes and expects to read.
  static const int currentFormatVersion = 1;

  /// Creates a persisted client state. The defaults describe the empty state a fresh install
  /// starts from, before any client ID has been generated.
  const PersistedClientState({
    this.clientId,
    this.credential,
    this.recoveryState = PairingRecoveryState.none,
  });

  /// The stable local client/installation identity, or `null` before one has been generated.
  final String? clientId;

  /// The trusted-device credential a completed pairing issued, or `null` before pairing.
  final String? credential;

  /// The current pairing recovery standing.
  final PairingRecoveryState recoveryState;

  /// Returns a copy of this state with the given fields replaced. To explicitly clear a field to
  /// `null` (for example discarding a stale credential), construct a new [PersistedClientState]
  /// directly instead -- this only supports additive field replacement.
  PersistedClientState copyWith({
    String? clientId,
    String? credential,
    PairingRecoveryState? recoveryState,
  }) => PersistedClientState(
    clientId: clientId ?? this.clientId,
    credential: credential ?? this.credential,
    recoveryState: recoveryState ?? this.recoveryState,
  );

  /// Compares persisted identity, credential, and recovery state values.
  @override
  bool operator ==(Object other) =>
      other is PersistedClientState &&
      other.clientId == clientId &&
      other.credential == credential &&
      other.recoveryState == recoveryState;

  /// Combines persisted identity, credential, and recovery state values.
  @override
  int get hashCode => Object.hash(clientId, credential, recoveryState);
}
