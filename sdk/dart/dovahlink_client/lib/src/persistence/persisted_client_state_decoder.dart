import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/host_identity_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Decodes and validates the SDK-owned persisted client-state format.
class PersistedClientStateDecoder {
  /// Decodes [json] or throws [DovahLinkStorageException] for an unsupported or malformed state.
  static PersistedClientState decode(JsonMap json) {
    final Object? formatVersion = json['formatVersion'];
    if (formatVersion != 1 &&
        formatVersion != PersistedClientState.currentFormatVersion) {
      throw DovahLinkStorageException(
        'Unsupported persisted client state format version: $formatVersion.',
      );
    }

    final Object? recoveryStateRaw = json['recoveryState'];
    final PairingRecoveryState recoveryState = switch (recoveryStateRaw) {
      'none' => PairingRecoveryState.none,
      'confirming' => PairingRecoveryState.confirming,
      _ => throw DovahLinkStorageException(
        'Unrecognized persisted recoveryState: $recoveryStateRaw.',
      ),
    };

    final Object? clientId = json['clientId'];
    if (clientId != null && clientId is! String) {
      throw const DovahLinkStorageException(
        'Persisted clientId is not a string.',
      );
    }
    final Object? credential = json['credential'];
    if (credential != null && credential is! String) {
      throw const DovahLinkStorageException(
        'Persisted credential is not a string.',
      );
    }

    final DovahLinkHost? knownHost = formatVersion == 1
        ? null
        : decodePersistedKnownHost(json['knownHost']);

    return PersistedClientState(
      clientId: clientId as String?,
      credential: credential as String?,
      recoveryState: recoveryState,
      knownHost: knownHost,
    );
  }
}

/// Decodes the optional v2 Host association, rejecting partial or invalid values.
/// @param raw The stored nested Host object, or `null` when no Host is known.
/// @return The validated Host association, or `null` when none is stored.
DovahLinkHost? decodePersistedKnownHost(Object? raw) {
  if (raw == null) {
    return null;
  }
  if (raw is! Map<String, dynamic> ||
      !raw.containsKey('hostId') ||
      !raw.containsKey('hostName') ||
      !raw.containsKey('endpoint')) {
    throw const DovahLinkStorageException(
      'Persisted knownHost must contain hostId, hostName, and endpoint.',
    );
  }

  final Object? hostId = raw['hostId'];
  final Object? hostName = raw['hostName'];
  final Object? endpointValue = raw['endpoint'];
  if (hostId is! String || !isValidHostId(hostId)) {
    throw const DovahLinkStorageException(
      'Persisted knownHost.hostId is not a valid UUID.',
    );
  }
  if (hostName is! String || !isValidHostName(hostName)) {
    throw const DovahLinkStorageException(
      'Persisted knownHost.hostName is invalid.',
    );
  }
  if (endpointValue is! String) {
    throw const DovahLinkStorageException(
      'Persisted knownHost.endpoint is not a string.',
    );
  }

  final Uri endpoint;
  try {
    endpoint = Uri.parse(endpointValue);
    if (!endpoint.hasAuthority ||
        (endpoint.scheme != 'ws' && endpoint.scheme != 'wss') ||
        endpoint.host.isEmpty ||
        endpoint.userInfo.isNotEmpty ||
        endpoint.fragment.isNotEmpty ||
        endpoint.port < 1 ||
        endpoint.port > 65535) {
      throw const FormatException();
    }
  } on FormatException {
    throw const DovahLinkStorageException(
      'Persisted knownHost.endpoint is not a valid WebSocket URI.',
    );
  }

  return DovahLinkHost(hostId: hostId, hostName: hostName, endpoint: endpoint);
}
