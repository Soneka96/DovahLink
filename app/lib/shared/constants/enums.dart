/// Every enum in the app lives here, regardless of which feature uses it.
library;

/// Describes the current lifecycle of the bridge connection.
enum ConnectionPhase {
  /// Sentinel value indicating that no phase was selected.
  none,

  /// No connection attempt is active.
  disconnected,

  /// The client is opening a transport connection.
  connecting,

  /// The client is negotiating protocol and capabilities.
  negotiating,

  /// The client has an accepted session and current state.
  connected,

  /// A newly accepted v2 session's bridge/play-context identity does not
  /// match what was previously cached; any state cached under the old
  /// identity must be refreshed before it can be trusted again.
  stale,

  /// The client is rebuilding state from a fresh snapshot.
  recovering,

  /// The bridge or transport cannot currently provide current state.
  unavailable,

  /// The peers cannot agree on a supported protocol version.
  incompatible;

  /// Returns the concise user-visible label for this phase.
  String get label => switch (this) {
    ConnectionPhase.none => 'Unknown',
    ConnectionPhase.disconnected => 'Disconnected',
    ConnectionPhase.connecting => 'Connecting',
    ConnectionPhase.negotiating => 'Negotiating',
    ConnectionPhase.connected => 'Connected',
    ConnectionPhase.stale => 'Stale',
    ConnectionPhase.recovering => 'Recovering',
    ConnectionPhase.unavailable => 'Unavailable',
    ConnectionPhase.incompatible => 'Incompatible',
  };
}

/// Describes the current lifecycle of the local device pairing flow.
enum PairingPhase {
  /// Sentinel value indicating that no phase was selected.
  none,

  /// The client is opening a transport connection and authenticating.
  connecting,

  /// The client could not reach the bridge (a transport-level failure, not a
  /// rejected pairing attempt). Distinct from [failed] per
  /// `ai/context/flutter/architecture.md`'s "Connection and recovery state".
  disconnected,

  /// The session authenticated without a trusted credential; pairing has not
  /// been requested yet.
  unpaired,

  /// The client is asking the bridge to start or query a pairing challenge.
  requestingCode,

  /// A pairing challenge is active; the user may enter the code shown in
  /// Skyrim.
  awaitingCode,

  /// The client is submitting the entered code and completing the trust
  /// handshake.
  confirming,

  /// The client holds a trusted credential for this bridge.
  trusted,

  /// The most recent pairing attempt failed with a user-safe error message.
  failed;

  /// Returns the concise user-visible label for this phase.
  String get label => switch (this) {
    PairingPhase.none => 'Unknown',
    PairingPhase.connecting => 'Connecting',
    PairingPhase.disconnected => 'Waiting for bridge',
    PairingPhase.unpaired => 'Not paired',
    PairingPhase.requestingCode => 'Requesting code',
    PairingPhase.awaitingCode => 'Awaiting code',
    PairingPhase.confirming => 'Confirming',
    PairingPhase.trusted => 'Paired',
    PairingPhase.failed => 'Failed',
  };
}

/// The bridge connection's status while a trusted pairing session is active, observed from the
/// SDK's full `connectionStateChanges` feed rather than a narrower administrative-only slice.
enum PairingConnectionStatus {
  /// Ordinary transport loss; the SDK's own bounded recovery may still succeed without user
  /// action.
  lost,

  /// The connection recovered after [lost].
  restored,

  /// The bridge administratively ended this session (revoked, blocked, trust reset, or factory
  /// reset). Terminal for the current session; recovery is always an explicit user action.
  invalidated,
}
