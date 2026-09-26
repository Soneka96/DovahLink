/// Host identity and metadata asserted by `hello_ack` or read from SDK persistence, paired with
/// the location used to reach it. This value does not authenticate a peer or prove it owns a
/// previously known Host identity.
final class DovahLinkHost {
  /// The stable DovahLink Host installation identity.
  final String hostId;

  /// The current OS computer name reported by the Host; mutable display metadata.
  final String hostName;

  /// The current connection location. It is not part of Host identity.
  final Uri endpoint;

  /// Creates a Host identity and metadata value.
  /// @param hostId The stable identity reported by the Host.
  /// @param hostName The Host's current OS computer name.
  /// @param endpoint The connection location used to reach this Host.
  const DovahLinkHost({
    required this.hostId,
    required this.hostName,
    required this.endpoint,
  });

  /// Compares the stable identity, mutable metadata, and endpoint values.
  @override
  bool operator ==(Object other) =>
      other is DovahLinkHost &&
      other.hostId == hostId &&
      other.hostName == hostName &&
      other.endpoint == endpoint;

  /// Combines the Host identity and metadata values.
  @override
  int get hashCode => Object.hash(hostId, hostName, endpoint);
}
