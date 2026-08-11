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
    ConnectionPhase.recovering => 'Recovering',
    ConnectionPhase.unavailable => 'Unavailable',
    ConnectionPhase.incompatible => 'Incompatible',
  };
}
