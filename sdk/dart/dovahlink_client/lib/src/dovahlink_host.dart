/// Host values asserted by a peer's protocol-validated `hello_ack`, paired with the location used
/// to reach it. This value does not authenticate the peer or prove it owns a previously known Host
/// identity.
final class DovahLinkHost {
  /// Creates a discovered Host value.
  /// @param hostId The stable identity reported by the Host.
  /// @param hostName The Host's current OS computer name.
  /// @param endpoint The connection location used to reach this Host.
  const DovahLinkHost({
    required this.hostId,
    required this.hostName,
    required this.endpoint,
  });

  /// The stable DovahLink Host installation identity asserted by the responding peer. This claim
  /// is not cryptographic proof that the peer owns a previously known Host identity.
  final String hostId;

  /// The current OS computer name reported by the Host; mutable display metadata.
  final String hostName;

  /// The current connection location. It is not part of Host identity.
  final Uri endpoint;
}
