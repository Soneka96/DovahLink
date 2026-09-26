import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The SDK-owned persisted client identity, credential, recovery state, and Known Host. Versioned
/// so an [IClientStorage] implementation can detect unsupported formats instead of misinterpreting
/// them. See `ai/context/sdk/persistence.md`.
class PersistedClientState {
  /// The current persisted-state format version this SDK writes; supported legacy formats are also
  /// read.
  static const int currentFormatVersion = 2;

  /// Creates a persisted client state. The defaults describe the empty state a fresh install
  /// starts from, before any client ID has been generated.
  const PersistedClientState({
    this.clientId,
    this.credential,
    this.recoveryState = PairingRecoveryState.none,
    this.knownHost,
  });

  /// The stable local client/installation identity, or `null` before one has been generated.
  final String? clientId;

  /// The trusted-device credential a completed pairing issued, or `null` before pairing.
  final String? credential;

  /// The current pairing recovery standing.
  final PairingRecoveryState recoveryState;

  /// The Host previously associated with this client, or `null` before pairing establishes one.
  final DovahLinkHost? knownHost;

  /// Returns a copy of this state with the given fields replaced. To explicitly clear a field to
  /// `null` (for example discarding a stale credential), construct a new [PersistedClientState]
  /// directly instead -- this only supports additive field replacement.
  PersistedClientState copyWith({
    String? clientId,
    String? credential,
    PairingRecoveryState? recoveryState,
    DovahLinkHost? knownHost,
  }) => PersistedClientState(
    clientId: clientId ?? this.clientId,
    credential: credential ?? this.credential,
    recoveryState: recoveryState ?? this.recoveryState,
    knownHost: knownHost ?? this.knownHost,
  );

  /// Compares persisted identity, credential, and recovery state values.
  @override
  bool operator ==(Object other) =>
      other is PersistedClientState &&
      other.clientId == clientId &&
      other.credential == credential &&
      other.recoveryState == recoveryState &&
      other.knownHost == knownHost;

  /// Combines persisted identity, credential, and recovery state values.
  @override
  int get hashCode =>
      Object.hash(clientId, credential, recoveryState, knownHost);
}
