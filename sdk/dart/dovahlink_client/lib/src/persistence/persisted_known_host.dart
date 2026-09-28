import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';

/// One persisted Host relationship and the bearer credential issued by that Host.
class PersistedKnownHost {
  /// The Host identity and its last-known metadata.
  final DovahLinkHost host;

  /// The bearer credential issued by [host], or `null` when no credential is stored.
  final String? credential;

  /// Creates a relationship for [host] with its optional current credential.
  /// @param host The Host identity and last-known metadata.
  /// @param credential The bearer credential issued by [host], if one is current.
  const PersistedKnownHost({required this.host, this.credential});

  /// Compares the relationship metadata and credential.
  @override
  bool operator ==(Object other) =>
      other is PersistedKnownHost &&
      other.host == host &&
      other.credential == credential;

  /// Combines the relationship metadata and credential.
  @override
  int get hashCode => Object.hash(host, credential);
}
