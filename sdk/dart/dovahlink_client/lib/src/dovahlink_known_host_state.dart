import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// A durable Known Host's metadata together with its current runtime availability.
final class DovahLinkKnownHostState {
  /// The durable Host identity and metadata.
  final DovahLinkHost host;

  /// The SDK's current runtime evidence about the Host's reachability.
  final DovahLinkHostAvailability availability;

  /// The session lifecycle for this exact Known Host relationship.
  final DovahLinkKnownHostSessionState sessionState;

  /// Whether the Host last reported that this relationship must pair again.
  /// This is a recovery hint, not current trust.
  final bool pairingRequired;

  /// Creates a complete Known Host runtime projection.
  /// @param host The durable Host metadata.
  /// @param availability The current runtime reachability evidence.
  /// @param sessionState The current session lifecycle for this relationship.
  /// @param pairingRequired Whether the Host last reported that pairing is required.
  const DovahLinkKnownHostState({
    required this.host,
    required this.availability,
    this.sessionState = DovahLinkKnownHostSessionState.disconnected,
    this.pairingRequired = false,
  });

  /// Compares the Host metadata and runtime availability.
  @override
  bool operator ==(Object other) =>
      other is DovahLinkKnownHostState &&
      other.host == host &&
      other.availability == availability &&
      other.sessionState == sessionState &&
      other.pairingRequired == pairingRequired;

  /// Combines the Host metadata and runtime availability values.
  @override
  int get hashCode =>
      Object.hash(host, availability, sessionState, pairingRequired);
}
