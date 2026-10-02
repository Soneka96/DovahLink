import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// An administrative invalidation for one admitted Known Host session.
final class DovahLinkKnownHostInvalidation {
  /// The Known Host relationship whose session was invalidated.
  final DovahLinkHostId hostId;

  /// The Host-reported administrative invalidation reason.
  final AdministrativeInvalidationReason reason;

  /// Creates an immutable invalidation event tied to its Known Host.
  /// @param hostId The durable Known Host relationship that was invalidated.
  /// @param reason The administrative reason reported by the Host.
  const DovahLinkKnownHostInvalidation({
    required this.hostId,
    required this.reason,
  });
}
