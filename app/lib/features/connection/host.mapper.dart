import 'package:dovahlink_client_sdk/dovahlink_client.dart' show DovahLinkHost;

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';

/// Maps typed SDK Host values into the app-owned Host representation.
final class HostMapper {
  /// Prevents construction of this static mapping boundary.
  const HostMapper._();

  /// Converts a validated SDK [host] into app-owned Host semantics.
  /// @param host The Host value returned by the SDK.
  /// @return The corresponding app-owned Host value.
  static Host fromSdk(DovahLinkHost host) =>
      Host(hostId: host.hostId, displayName: host.hostName, uri: host.endpoint);
}
