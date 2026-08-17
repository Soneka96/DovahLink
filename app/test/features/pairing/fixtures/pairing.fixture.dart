import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';

/// Builds a reusable pairing-handshake result for tests. Defaults to a
/// trusted handshake; override individual fields per test case.
PairingHandshakeEntity buildPairingHandshakeEntity({
  String bridgeVersion = '1.2.3',
  bool trusted = true,
  String? credentialRejectedMessage,
}) => PairingHandshakeEntity(
  bridgeVersion: bridgeVersion,
  trusted: trusted,
  credentialRejectedMessage: credentialRejectedMessage,
);
