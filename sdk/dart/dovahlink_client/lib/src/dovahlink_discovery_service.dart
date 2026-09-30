import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/host_presence_probe.dart';

/// The SDK's current loopback endpoint for the public Host listener.
final Uri _localHostEndpoint = Uri.parse('ws://127.0.0.1:58231/');

/// Defines one low-level local Host metadata probe used by the client.
abstract interface class IDovahLinkDiscoveryService {
  /// Returns the validated local Host claim before Known Host reconciliation.
  ///
  /// The client owns candidate membership. An empty list means the endpoint could not be reached
  /// or the request timed out. A claim's Host ID is unauthenticated.
  /// @throws [DovahLinkConnectionException] if an HTTP response rejects the probe.
  /// @throws [DovahLinkProtocolException] if the response is malformed.
  /// @throws [DovahLinkCompatibilityException] if the Host version is unsupported.
  Future<List<DovahLinkHost>> discover();
}

/// Reads the local Host claim through its sessionless metadata probe.
class DovahLinkDiscoveryService implements IDovahLinkDiscoveryService {
  /// The candidate location this service probes.
  final Uri _endpoint;

  /// Reads and validates the unauthenticated Host claim without opening a protocol session.
  final IHostPresenceProbe _hostPresenceProbe;

  /// Creates the local discovery service.
  /// @param hostPresenceProbe Reads the unauthenticated Host claim at the local endpoint.
  DovahLinkDiscoveryService({required IHostPresenceProbe hostPresenceProbe})
    : this._(
        endpoint: _localHostEndpoint,
        hostPresenceProbe: hostPresenceProbe,
      );

  /// Creates a service for the supplied endpoint.
  DovahLinkDiscoveryService._({
    required Uri endpoint,
    required IHostPresenceProbe hostPresenceProbe,
  }) : _endpoint = endpoint,
       _hostPresenceProbe = hostPresenceProbe;

  /// Returns the Host metadata asserted by the local endpoint for client reconciliation.
  /// Validation does not authenticate a peer as an installation previously known under
  /// [DovahLinkHost.hostId].
  ///
  /// The endpoint is the current location, not part of identity. This request sends no credential,
  /// creates no session, and never pairs or reconnects. A discovered `hostId` alone must not
  /// authorize trust, credential disclosure, pairing bypass, or another security-sensitive decision.
  /// @return The validated Host claim, or an empty list when the endpoint cannot be reached or the
  /// request times out.
  /// @throws [DovahLinkConnectionException] if an HTTP response rejects the probe.
  /// @throws [DovahLinkProtocolException] if a reachable endpoint returns malformed metadata.
  /// @throws [DovahLinkCompatibilityException] if the endpoint reports an unsupported Host version.
  @override
  Future<List<DovahLinkHost>> discover() async {
    try {
      return <DovahLinkHost>[await _hostPresenceProbe.probe(_endpoint)];
    } on DovahLinkConnectionException catch (error) {
      if (error.httpStatusCode == null) {
        return const <DovahLinkHost>[];
      }
      rethrow;
    }
  }
}

/// Builds a discovery service for SDK tests without changing the public local endpoint.
/// @param endpoint The test Host endpoint to probe.
/// @param hostPresenceProbe The probe implementation to use.
/// @return A discovery service configured for the test endpoint.
@visibleForTesting
DovahLinkDiscoveryService buildDovahLinkDiscoveryServiceForTesting({
  required Uri endpoint,
  required IHostPresenceProbe hostPresenceProbe,
}) => DovahLinkDiscoveryService._(
  endpoint: endpoint,
  hostPresenceProbe: hostPresenceProbe,
);
