import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The SDK's current loopback endpoint for the public Host listener.
final Uri _localHostEndpoint = Uri.parse('ws://127.0.0.1:58231/');

/// Defines the SDK's local Host discovery capability.
abstract interface class IDovahLinkDiscoveryService {
  /// Probes the local endpoint and returns a handshake-confirmed Host, or `null` when no listener
  /// can be reached.
  /// @throws [DovahLinkProtocolException] if a reachable peer violates the DovahLink protocol.
  /// @throws [DovahLinkCompatibilityException] if a reachable Host version is unsupported.
  Future<DovahLinkHost?> discoverLocalHost();
}

/// Discovers the local Host by opening an isolated, unpaired SDK session and validating its
/// authoritative `hello_ack` response.
class DovahLinkDiscoveryService implements IDovahLinkDiscoveryService {
  /// The candidate location this service probes.
  final Uri _endpoint;

  /// Central SDK request bounds, overridable only by the internal test factory.
  final Map<TimeoutClass, Duration> _timeoutDurations;

  /// Creates a local loopback discovery service.
  DovahLinkDiscoveryService()
    : this._(
        endpoint: _localHostEndpoint,
        timeoutDurations: kTimeoutClassDurations,
      );

  /// Creates a service for the supplied endpoint and SDK timeout policy.
  DovahLinkDiscoveryService._({
    required Uri endpoint,
    required Map<TimeoutClass, Duration> timeoutDurations,
  }) : _endpoint = endpoint,
       _timeoutDurations = timeoutDurations;

  /// Probes the local endpoint, returning `null` when no listener can be reached.
  ///
  /// The returned [DovahLinkHost.hostId] and [DovahLinkHost.hostName] are taken from the
  /// connected Host's validated `hello_ack`; the endpoint is only its current location. The probe
  /// uses transient storage and never presents consumer credentials, pairs, or reconnects.
  /// @return The confirmed Host, or `null` when the candidate cannot be connected.
  /// @throws [DovahLinkProtocolException] if a reachable peer violates the DovahLink protocol.
  /// @throws [DovahLinkCompatibilityException] if a reachable Host version is unsupported.
  @override
  Future<DovahLinkHost?> discoverLocalHost() async {
    final DovahLinkClient client = buildDovahLinkClientForDiscovery(
      timeoutDurations: _timeoutDurations,
    );
    try {
      try {
        await client.connect(_endpoint);
      } on DovahLinkConnectionException {
        return null;
      }
      final HelloResult hello = await client.hello();
      return DovahLinkHost(
        hostId: hello.hostId,
        hostName: hello.hostName,
        endpoint: _endpoint,
      );
    } finally {
      await client.disconnect();
    }
  }
}

/// Builds a discovery service for SDK tests without changing the public endpoint or timeout API.
/// @param endpoint The test WebSocket endpoint to probe.
/// @param timeoutDurations The bounded request durations used by the probe.
/// @return A discovery service configured for the test endpoint.
@visibleForTesting
DovahLinkDiscoveryService buildDovahLinkDiscoveryServiceForTesting({
  required Uri endpoint,
  Map<TimeoutClass, Duration> timeoutDurations = kTimeoutClassDurations,
}) => DovahLinkDiscoveryService._(
  endpoint: endpoint,
  timeoutDurations: timeoutDurations,
);
