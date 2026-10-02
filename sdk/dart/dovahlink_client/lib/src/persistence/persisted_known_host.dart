import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';

/// One persisted Host relationship, its bearer credential, and its last-known recovery hint.
class PersistedKnownHost {
  /// The Host identity and its last-known metadata.
  final DovahLinkHost host;

  /// The bearer credential issued by [host], or `null` when no credential is stored.
  final String? credential;

  /// Whether the Host previously reported that this relationship must pair again.
  /// This is a last-known UI hint, not current trust.
  final bool pairingRequired;

  /// Creates a relationship for [host] with its optional credential and recovery hint.
  /// @param host The Host identity and last-known metadata.
  /// @param credential The bearer credential issued by [host], if one is current.
  /// @param pairingRequired Whether a Host previously reported that pairing is required.
  const PersistedKnownHost({
    required this.host,
    this.credential,
    this.pairingRequired = false,
  });

  /// Compares the relationship metadata and credential.
  @override
  bool operator ==(Object other) =>
      other is PersistedKnownHost &&
      other.host == host &&
      other.credential == credential &&
      other.pairingRequired == pairingRequired;

  /// Combines the relationship metadata and credential.
  @override
  int get hashCode => Object.hash(host, credential, pairingRequired);
}
