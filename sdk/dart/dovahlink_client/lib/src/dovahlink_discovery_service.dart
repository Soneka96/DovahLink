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

/// Defines the SDK's Host discovery capability.
abstract interface class IDovahLinkDiscoveryService {
  /// Returns every discovered Host candidate after protocol and compatibility validation.
  ///
  /// An empty list means no candidate was found. A candidate's claimed identity does not
  /// authenticate it or prove ownership of a previously known identity.
  /// @throws [DovahLinkConnectionException] if an HTTP response rejects the WebSocket upgrade, or a
  ///     connected peer drops the connection or does not answer `hello` before the request timeout.
  /// @throws [DovahLinkProtocolException] if a reachable peer violates the DovahLink protocol.
  /// @throws [DovahLinkCompatibilityException] if a reachable Host version is unsupported.
  Future<List<DovahLinkHost>> discover();
}

/// Finds Host candidates by validating unpaired `hello_ack` responses with the normal SDK stack.
/// Discovery validates a peer's Host claim; it does not authenticate Host identity.
class DovahLinkDiscoveryService implements IDovahLinkDiscoveryService {
  /// The candidate location this service probes.
  final Uri _endpoint;

  /// Central SDK request bounds, overridable only by the internal test factory.
  final Map<TimeoutClass, Duration> _timeoutDurations;

  /// Creates the discovery service with its current local probe strategy.
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

  /// Returns the Host values asserted by each validated `hello_ack`. The current strategy probes
  /// the local endpoint. Protocol and compatibility validation do not authenticate a peer as an
  /// installation previously known under [DovahLinkHost.hostId].
  ///
  /// The endpoint is the current location, not part of identity. This probe uses transient storage,
  /// sends an unpaired `hello`, and never presents consumer credentials, pairs, or reconnects. A
  /// discovered `hostId` alone must not authorize trust, credential disclosure, pairing bypass, or
  /// another security-sensitive decision.
  /// @return The validated Host candidates, or an empty list when WebSocket setup fails without an
  ///     HTTP status code.
  /// @throws [DovahLinkConnectionException] if an HTTP response rejects the WebSocket upgrade, or a
  ///     connected peer drops the connection or does not answer `hello` before the request timeout.
  /// @throws [DovahLinkProtocolException] if a reachable peer violates the DovahLink protocol.
  /// @throws [DovahLinkCompatibilityException] if a reachable peer reports an unsupported Host
  ///     version.
  @override
  Future<List<DovahLinkHost>> discover() async {
    final DovahLinkClient client = buildDovahLinkClientForDiscovery(
      timeoutDurations: _timeoutDurations,
    );
    try {
      try {
        await client.connect(_endpoint);
      } on DovahLinkConnectionException catch (error) {
        if (error.httpStatusCode != null) {
          rethrow;
        }
        return const <DovahLinkHost>[];
      }
      final HelloResult hello = await client.hello();
      return <DovahLinkHost>[
        DovahLinkHost(
          hostId: hello.hostId,
          hostName: hello.hostName,
          endpoint: _endpoint,
        ),
      ];
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
