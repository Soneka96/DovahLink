import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

/// Central test-owned catalog of representative Flutter app values.
abstract final class Fixtures {
  // ---- Connection ----

  /// Builds a Bridge identity with the representative local endpoint.
  static BridgeEntity buildBridgeEntity({
    /// The user-facing Bridge name.
    String displayName = 'Local Bridge',

    /// The Bridge endpoint, or the representative local endpoint when omitted.
    Uri? uri,
  }) => BridgeEntity(displayName: displayName, uri: uri ?? defaultBridgeUri);

  // ---- Pairing ----

  /// Builds a pairing handshake with representative trusted-session defaults.
  static PairingHandshakeEntity buildPairingHandshakeEntity({
    /// The Bridge/mod release version reported by the handshake.
    String bridgeVersion = '1.2.3',

    /// Whether the session already holds a trusted credential.
    bool trusted = true,

    /// The user-safe explanation for a rejected credential, when applicable.
    String? credentialRejectedMessage,
  }) => PairingHandshakeEntity(
    bridgeVersion: bridgeVersion,
    trusted: trusted,
    credentialRejectedMessage: credentialRejectedMessage,
  );
}
