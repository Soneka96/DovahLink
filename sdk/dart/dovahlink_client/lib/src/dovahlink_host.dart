/// A Host identity confirmed by its `hello_ack` response, paired with the location used to reach
/// it for this discovery operation.
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

  /// The stable DovahLink-generated identity of the Host installation.
  final String hostId;

  /// The current OS computer name reported by the Host; mutable display metadata.
  final String hostName;

  /// The current connection location. It is not part of Host identity.
  final Uri endpoint;
}
