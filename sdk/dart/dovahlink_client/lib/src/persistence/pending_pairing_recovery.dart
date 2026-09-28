import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The one persisted pairing recovery operation and the Host that owns it.
class PendingPairingRecovery {
  /// The stable identity of the Host whose credential is being confirmed.
  final String hostId;

  /// The current pairing recovery phase.
  final PairingRecoveryState state;

  /// Creates a recovery operation for [hostId] in [state].
  /// @param hostId The stable ID of the Host that owns this operation.
  /// @param state The operation's current recovery phase.
  const PendingPairingRecovery({required this.hostId, required this.state});

  /// Compares the recovery owner and phase.
  @override
  bool operator ==(Object other) =>
      other is PendingPairingRecovery &&
      other.hostId == hostId &&
      other.state == state;

  /// Combines the recovery owner and phase.
  @override
  int get hashCode => Object.hash(hostId, state);
}
