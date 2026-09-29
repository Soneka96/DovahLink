import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/compatibility/host_version_compatibility.dart';
import 'package:dovahlink_client_sdk/src/protocol/host_identity_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Probes one Host endpoint without creating a DovahLink protocol session.
abstract interface class IHostPresenceProbe {
  /// Returns the valid, compatible Host claim served at [endpoint].
  ///
  /// The returned identity is unauthenticated and must not authorize Known Host credentials or
  /// trust changes.
  /// @param endpoint The Host's WebSocket endpoint; only its scheme, host, and port are used.
  /// @throws [DovahLinkConnectionException] if the endpoint cannot be reached or rejects the probe.
  /// @throws [DovahLinkProtocolException] if the response is malformed or exceeds its size bound.
  /// @throws [DovahLinkCompatibilityException] if the claimed Host version is unsupported.
  Future<DovahLinkHost> probe(Uri endpoint);
}

/// Reads public Host metadata through the bounded sessionless HTTP probe endpoint.
class HostPresenceProbe implements IHostPresenceProbe {
  /// The total time allowed for the request and its complete response body.
  final Duration _timeout;

  /// Creates a probe with the SDK's central Host-probe timeout.
  HostPresenceProbe() : _timeout = kHostProbeTimeout;

  /// Creates a probe with a bounded test timeout.
  @visibleForTesting
  HostPresenceProbe.forTesting({required Duration timeout})
    : _timeout = timeout;

  /// Implements [IHostPresenceProbe.probe].
  @override
  Future<DovahLinkHost> probe(Uri endpoint) async {
    if (endpoint.scheme != 'ws' && endpoint.scheme != 'wss') {
      throw ArgumentError.value(endpoint, 'endpoint', 'must use ws or wss');
    }
    final Uri probeUri = Uri(
      scheme: endpoint.scheme == 'wss' ? 'https' : 'http',
      host: endpoint.host,
      port: endpoint.port,
      path: '/.well-known/dovahlink',
    );
    final HttpClient client = HttpClient()..connectionTimeout = _timeout;
    try {
      return await (() async {
        final HttpClientRequest request = await client.getUrl(probeUri);
        request.followRedirects = false;
        final HttpClientResponse response = await request.close();
        if (response.statusCode != HttpStatus.ok) {
          throw DovahLinkConnectionException(
            'The Host probe returned HTTP ${response.statusCode}.',
            httpStatusCode: response.statusCode,
          );
        }
        if (response.contentLength > kHostProbeResponseMaxBytes) {
          throw const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'The Host probe response exceeded its size limit.',
            retryable: false,
          );
        }

        final BytesBuilder body = BytesBuilder(copy: false);
        await for (final List<int> chunk in response) {
          if (body.length + chunk.length > kHostProbeResponseMaxBytes) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'The Host probe response exceeded its size limit.',
              retryable: false,
            );
          }
          body.add(chunk);
        }

        final Object? decoded = jsonDecode(utf8.decode(body.takeBytes()));
        if (decoded is! JsonMap) {
          throw const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'The Host probe response must be a JSON object.',
            retryable: false,
          );
        }
        final Object? rawHostId = decoded['hostId'];
        final Object? rawHostName = decoded['hostName'];
        final Object? rawHostVersion = decoded['hostVersion'];
        if (rawHostId is! String ||
            rawHostName is! String ||
            rawHostVersion is! String ||
            !isValidHostId(rawHostId) ||
            !isValidHostName(rawHostName) ||
            rawHostVersion.isEmpty) {
          throw const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'The Host probe response has invalid identity metadata.',
            retryable: false,
          );
        }
        validateHostVersionCompatibility(rawHostVersion);
        return DovahLinkHost(
          hostId: DovahLinkHostId(rawHostId).value,
          hostName: rawHostName,
          endpoint: endpoint,
        );
      }()).timeout(_timeout);
    } on TimeoutException {
      throw const DovahLinkConnectionException('The Host probe timed out.');
    } on SocketException catch (error) {
      throw DovahLinkConnectionException('Could not reach the Host: $error');
    } on HttpException catch (error) {
      throw DovahLinkConnectionException('The Host probe failed: $error');
    } on IOException catch (error) {
      throw DovahLinkConnectionException('The Host probe failed: $error');
    } on FormatException {
      throw const DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'The Host probe response is not valid JSON.',
        retryable: false,
      );
    } finally {
      client.close(force: true);
    }
  }
}
