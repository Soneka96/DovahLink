import 'dart:math';

import 'package:shared_preferences/shared_preferences.dart';

/// Persists and resolves this installation's logical client identity.
///
/// The identity is a stable, opaque identifier for one client installation,
/// independent of any connection, socket, or transport. It is never
/// authentication proof or a trust credential.
abstract interface class ConnectionLocalDataSource {
  /// Returns the persisted client ID, generating and persisting a fresh one
  /// on first use. Regenerates after cleared app data or reinstall.
  Future<String> resolveClientId();
}

/// Persists the client ID via [SharedPreferences] and generates a fresh
/// RFC 4122 version-4 UUID with [Random.secure] when none is stored yet.
class ConnectionLocalDataSourceImpl
    implements ConnectionLocalDataSource {
  /// Creates a data source backed by [_preferences].
  ConnectionLocalDataSourceImpl(this._preferences);

  /// The [SharedPreferences] key the client ID is stored under.
  static const String preferencesKey = 'connection.client_identity_id';

  /// Backing persistent storage for the client ID.
  final SharedPreferences _preferences;

  /// Source of randomness for generating a fresh client ID.
  final Random _random = Random.secure();

  /// See [ConnectionLocalDataSource.resolveClientId].
  // ponytail: not memoized or lock-guarded against concurrent first-time
  // callers racing to mint and persist different IDs; no call site exists
  // yet. Add memoization once this is wired behind a shared DI instance.
  @override
  Future<String> resolveClientId() async {
    final String? existing = _preferences.getString(preferencesKey);
    if (existing != null && existing.isNotEmpty) {
      return existing;
    }

    final String generated = _generateUuidV4();
    await _preferences.setString(preferencesKey, generated);
    return generated;
  }

  /// Generates a random RFC 4122 version-4 UUID string.
  String _generateUuidV4() {
    final List<int> bytes = List<int>.generate(16, (_) => _random.nextInt(256));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;

    String hexRange(int start, int end) => bytes
        .sublist(start, end)
        .map((int byte) => byte.toRadixString(16).padLeft(2, '0'))
        .join();

    return '${hexRange(0, 4)}-${hexRange(4, 6)}-${hexRange(6, 8)}-'
        '${hexRange(8, 10)}-${hexRange(10, 16)}';
  }
}
