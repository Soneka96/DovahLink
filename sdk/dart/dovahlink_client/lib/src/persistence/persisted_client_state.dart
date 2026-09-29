import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/persistence/pending_pairing_recovery.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';

/// The SDK-owned client identity and Host relationships.
class PersistedClientState {
  /// The current persisted-state format version this SDK writes.
  static const int currentFormatVersion = 3;

  /// The stable local client identity, or `null` before one has been generated.
  final String? clientId;

  /// Known Host relationships keyed by the stable Host ID.
  final Map<String, PersistedKnownHost> knownHosts;

  /// The single active pairing recovery operation, if any.
  final PendingPairingRecovery? pendingPairingRecovery;

  /// Creates client state with immutable, Host-ID-keyed relationships.
  /// @param clientId The stable local Client ID, or `null` before it is generated.
  /// @param knownHosts The Host relationships to own, keyed by stable Host ID.
  /// @param pendingPairingRecovery The single active Host-owned recovery operation, if any.
  /// @throws [ArgumentError] if a relationship key is invalid or recovery has no Known Host.
  PersistedClientState({
    this.clientId,
    Map<String, PersistedKnownHost> knownHosts = const {},
    PendingPairingRecovery? pendingPairingRecovery,
  }) : knownHosts = _normalizeKnownHosts(knownHosts),
       pendingPairingRecovery = pendingPairingRecovery == null
           ? null
           : PendingPairingRecovery(
               hostId: DovahLinkHostId(pendingPairingRecovery.hostId).value,
               state: pendingPairingRecovery.state,
             ) {
    if (this.pendingPairingRecovery != null &&
        !this.knownHosts.containsKey(this.pendingPairingRecovery!.hostId)) {
      throw ArgumentError.value(
        pendingPairingRecovery,
        'pendingPairingRecovery',
        'must belong to a Known Host',
      );
    }
  }

  /// Returns a copy with selected values replaced.
  /// @param clientId The replacement local Client ID, or `null` to retain the current value.
  /// @param knownHosts The replacement complete Host map, or `null` to retain it.
  /// @param pendingPairingRecovery The recovery operation to set, if supplied.
  /// @param clearPendingPairingRecovery Whether to clear the current recovery operation.
  /// @return A new immutable persisted-state value.
  PersistedClientState copyWith({
    String? clientId,
    Map<String, PersistedKnownHost>? knownHosts,
    PendingPairingRecovery? pendingPairingRecovery,
    bool clearPendingPairingRecovery = false,
  }) => PersistedClientState(
    clientId: clientId ?? this.clientId,
    knownHosts: knownHosts ?? this.knownHosts,
    pendingPairingRecovery: clearPendingPairingRecovery
        ? null
        : pendingPairingRecovery ?? this.pendingPairingRecovery,
  );

  /// Compares client identity, Host relationships, and pending recovery.
  @override
  bool operator ==(Object other) =>
      other is PersistedClientState &&
      other.clientId == clientId &&
      _sameMap(other.knownHosts, knownHosts) &&
      other.pendingPairingRecovery == pendingPairingRecovery;

  /// Combines the state values.
  @override
  int get hashCode => Object.hash(
    clientId,
    Object.hashAllUnordered(
      knownHosts.entries.map(
        (MapEntry<String, PersistedKnownHost> entry) =>
            Object.hash(entry.key, entry.value),
      ),
    ),
    pendingPairingRecovery,
  );

  /// Compares two host-keyed relationship maps by key and value.
  /// @param left The first relationship map.
  /// @param right The second relationship map.
  /// @return Whether both maps contain equal records under the same Host IDs.
  static bool _sameMap(
    Map<String, PersistedKnownHost> left,
    Map<String, PersistedKnownHost> right,
  ) {
    if (left.length != right.length) {
      return false;
    }
    return left.entries.every(
      (MapEntry<String, PersistedKnownHost> entry) =>
          right[entry.key] == entry.value,
    );
  }

  /// Normalizes UUID-key casing and verifies each key matches its Host record.
  /// @param hosts The Host relationships to validate and normalize.
  /// @return An immutable map keyed by lowercase Host UUID.
  static Map<String, PersistedKnownHost> _normalizeKnownHosts(
    Map<String, PersistedKnownHost> hosts,
  ) {
    final Map<String, PersistedKnownHost> normalized = {};
    for (final MapEntry<String, PersistedKnownHost> entry in hosts.entries) {
      final String key = DovahLinkHostId(entry.key).value;
      if (key != DovahLinkHostId(entry.value.host.hostId).value ||
          normalized.containsKey(key)) {
        throw ArgumentError.value(
          hosts,
          'knownHosts',
          'keys must uniquely match Host IDs',
        );
      }
      final DovahLinkHost host = entry.value.host;
      normalized[key] = PersistedKnownHost(
        host: host.hostId == key
            ? host
            : DovahLinkHost(
                hostId: key,
                hostName: host.hostName,
                endpoint: host.endpoint,
              ),
        credential: entry.value.credential,
      );
    }
    return Map<String, PersistedKnownHost>.unmodifiable(normalized);
  }
}
