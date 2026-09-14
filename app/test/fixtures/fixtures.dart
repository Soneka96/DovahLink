import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

/// Central test-owned catalog of representative Flutter app values.
abstract final class Fixtures {
  // ---- Connection ----

  /// Builds a Host identity with the representative local endpoint.
  static HostEntity buildHostEntity({
    /// The user-facing Host name.
    String displayName = 'Local Host',

    /// The Host endpoint, or the representative local endpoint when omitted.
    Uri? uri,
  }) => HostEntity(displayName: displayName, uri: uri ?? defaultHostUri);

  // ---- Pairing ----

  /// Builds a pairing handshake with representative trusted-session defaults.
  static PairingHandshakeEntity buildPairingHandshakeEntity({
    /// The Host's own release version reported by the handshake.
    String hostVersion = '1.2.3',

    /// Whether the session already holds a trusted credential.
    bool trusted = true,

    /// The user-safe explanation for a rejected credential, when applicable.
    String? credentialRejectedMessage,
  }) => PairingHandshakeEntity(
    hostVersion: hostVersion,
    trusted: trusted,
    credentialRejectedMessage: credentialRejectedMessage,
  );
}
