/// The client's local recovery standing for an in-progress pairing confirmation, persisted so a
/// crash or relaunch between issuing a credential and confirming it can resume correctly. See
/// `ai/context/protocol/security.md`'s "Persistent local trust" recoverable confirmation handshake.
enum PairingRecoveryState {
  /// No pairing confirmation is outstanding.
  none,

  /// A credential was issued and durably saved, but final confirmation (`pairing_ack`) has not yet
  /// been acknowledged by the bridge as `trusted`/`already_trusted`.
  confirming,
}

/// The SDK-owned persisted client identity, credential, and pairing recovery state. Versioned so a
/// [ClientStorage] implementation can detect and reject a format it does not understand rather
/// than silently misinterpreting it. See `ai/context/sdk/persistence.md`.
class PersistedClientState {
  /// Creates a persisted client state. The defaults describe the empty state a fresh install
  /// starts from, before any client ID has been generated.
  const PersistedClientState({
    this.clientId,
    this.credential,
    this.recoveryState = PairingRecoveryState.none,
  });

  /// The current persisted-state format version this SDK writes and expects to read.
  static const int currentFormatVersion = 1;

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

  @override
  bool operator ==(Object other) =>
      other is PersistedClientState &&
      other.clientId == clientId &&
      other.credential == credential &&
      other.recoveryState == recoveryState;

  @override
  int get hashCode => Object.hash(clientId, credential, recoveryState);
}
