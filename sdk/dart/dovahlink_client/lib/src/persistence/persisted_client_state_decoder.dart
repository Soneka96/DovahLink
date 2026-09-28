import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/pending_pairing_recovery.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/protocol/host_identity_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Decodes and validates the SDK-owned persisted client-state format.
class PersistedClientStateDecoder {
  /// Decodes [json] or throws [DovahLinkStorageException] for unsupported or malformed state.
  /// @param json The decoded JSON object to validate.
  /// @return The typed persisted client state.
  /// @throws [DovahLinkStorageException] if the version or any persisted field is invalid.
  static PersistedClientState decode(JsonMap json) {
    final Object? formatVersion = json['formatVersion'];
    if (formatVersion != 1 &&
        formatVersion != 2 &&
        formatVersion != PersistedClientState.currentFormatVersion) {
      throw DovahLinkStorageException(
        'Unsupported persisted client state format version: $formatVersion.',
      );
    }
    final Object? clientId = json['clientId'];
    if (clientId != null && clientId is! String) {
      throw const DovahLinkStorageException(
        'Persisted clientId is not a string.',
      );
    }

    // Earlier development formats held one global bearer credential. They are intentionally
    // discarded at this pre-release cutover; preserving them would require guessing an owner.
    if (formatVersion != PersistedClientState.currentFormatVersion) {
      return PersistedClientState(clientId: clientId as String?);
    }

    final Map<String, PersistedKnownHost> knownHosts = _decodeKnownHosts(
      json['knownHosts'],
    );
    final PendingPairingRecovery? recovery = _decodeRecovery(
      json['pendingPairingRecovery'],
    );
    if (recovery != null &&
        !knownHosts.containsKey(recovery.hostId.toLowerCase())) {
      throw const DovahLinkStorageException(
        'Pending pairing recovery has no matching Known Host.',
      );
    }
    return PersistedClientState(
      clientId: clientId as String?,
      knownHosts: knownHosts,
      pendingPairingRecovery: recovery,
    );
  }

  /// Decodes the Host relationships keyed by validated Host IDs.
  /// @param raw The persisted `knownHosts` JSON object.
  /// @return Validated Host relationships keyed by normalized Host UUID.
  /// @throws [DovahLinkStorageException] if the collection or any relationship is malformed.
  static Map<String, PersistedKnownHost> _decodeKnownHosts(Object? raw) {
    if (raw is! Map<String, dynamic>) {
      throw const DovahLinkStorageException(
        'Persisted knownHosts is not an object.',
      );
    }
    final Map<String, PersistedKnownHost> hosts = {};
    final Set<String> normalizedIds = <String>{};
    for (final MapEntry<String, dynamic> entry in raw.entries) {
      if (!isValidHostId(entry.key) || entry.value is! Map<String, dynamic>) {
        throw const DovahLinkStorageException(
          'Persisted knownHosts contains an invalid Host entry.',
        );
      }
      final String hostId = entry.key.toLowerCase();
      if (!normalizedIds.add(hostId)) {
        throw const DovahLinkStorageException(
          'Persisted knownHosts contains duplicate Host IDs.',
        );
      }
      final Map<String, dynamic> value = entry.value as Map<String, dynamic>;
      final Object? hostName = value['hostName'];
      final Object? endpointValue = value['endpoint'];
      final Object? credential = value['credential'];
      if (hostName is! String || !isValidHostName(hostName)) {
        throw const DovahLinkStorageException(
          'Persisted Known Host name is invalid.',
        );
      }
      if (credential != null && credential is! String) {
        throw const DovahLinkStorageException(
          'Persisted Known Host credential is not a string.',
        );
      }
      if (endpointValue is! String) {
        throw const DovahLinkStorageException(
          'Persisted Known Host endpoint is not a string.',
        );
      }
      final Uri endpoint = _decodeEndpoint(endpointValue);
      hosts[hostId] = PersistedKnownHost(
        host: DovahLinkHost(
          hostId: hostId,
          hostName: hostName,
          endpoint: endpoint,
        ),
        credential: credential as String?,
      );
    }
    return hosts;
  }

  /// Decodes the optional Host-owned pairing recovery operation.
  /// @param raw The persisted recovery JSON object, or `null` when no operation is pending.
  /// @return The typed pending operation, or `null` when none is stored.
  /// @throws [DovahLinkStorageException] if the recovery owner or phase is invalid.
  static PendingPairingRecovery? _decodeRecovery(Object? raw) {
    if (raw == null) {
      return null;
    }
    if (raw is! Map<String, dynamic>) {
      throw const DovahLinkStorageException(
        'Persisted pendingPairingRecovery is not an object.',
      );
    }
    final Object? hostId = raw['hostId'];
    final Object? state = raw['state'];
    if (hostId is! String || !isValidHostId(hostId) || state != 'confirming') {
      throw const DovahLinkStorageException(
        'Persisted pendingPairingRecovery is invalid.',
      );
    }
    return PendingPairingRecovery(
      hostId: hostId,
      state: PairingRecoveryState.confirming,
    );
  }

  /// Parses and validates a persisted WebSocket endpoint.
  /// @param value The serialized endpoint URI.
  /// @return The validated WebSocket URI.
  /// @throws [DovahLinkStorageException] if [value] is not a valid WebSocket endpoint.
  static Uri _decodeEndpoint(String value) {
    try {
      final Uri endpoint = Uri.parse(value);
      if (!endpoint.hasAuthority ||
          (endpoint.scheme != 'ws' && endpoint.scheme != 'wss') ||
          endpoint.host.isEmpty ||
          endpoint.userInfo.isNotEmpty ||
          endpoint.fragment.isNotEmpty ||
          endpoint.port < 1 ||
          endpoint.port > 65535) {
        throw const FormatException();
      }
      return endpoint;
    } on FormatException {
      throw const DovahLinkStorageException(
        'Persisted Known Host endpoint is not a valid WebSocket URI.',
      );
    }
  }
}
