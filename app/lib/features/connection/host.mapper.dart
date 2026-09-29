import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkHost, DovahLinkHostAvailability, DovahLinkKnownHostState;

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Maps SDK Host and Known Host values into their app-owned representations.
final class HostMapper {
  /// Prevents construction of this static mapping boundary.
  const HostMapper._();

  /// Converts a validated SDK [host] into app-owned Host semantics.
  /// @param host The Host value returned by the SDK.
  /// @return The corresponding app-owned Host value.
  static Host fromSdk(DovahLinkHost host) =>
      Host(hostId: host.hostId, displayName: host.hostName, uri: host.endpoint);

  /// Converts a typed SDK Known Host state into the app-owned projection.
  /// @param knownHostState The complete Known Host state returned by the SDK.
  /// @return The corresponding app-owned Known Host value.
  static KnownHost fromSdkKnownHostState(
    DovahLinkKnownHostState knownHostState,
  ) => KnownHost(
    host: fromSdk(knownHostState.host),
    availability: switch (knownHostState.availability) {
      DovahLinkHostAvailability.unknown => HostAvailability.unknown,
      DovahLinkHostAvailability.online => HostAvailability.online,
      DovahLinkHostAvailability.offline => HostAvailability.offline,
    },
  );

  /// Converts SDK Host metadata into a Known Host with no reachability evidence.
  /// @param host The durable Host metadata returned by the SDK.
  /// @return The app-owned Known Host with unknown availability.
  static KnownHost fromSdkKnownHostMetadata(DovahLinkHost host) =>
      KnownHost(host: fromSdk(host), availability: HostAvailability.unknown);
}
