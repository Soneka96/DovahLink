import 'dart:math';

/// Generates cryptographically random RFC 4122 version-4 UUIDs.
class UuidV4Generator {
  /// Creates a generator backed by the platform's secure random source.
  UuidV4Generator();

  /// The secure random source used for UUID bytes.
  final Random _random = Random.secure();

  /// Generates one lowercase RFC 4122 version-4 UUID.
  String generate() {
    final List<int> bytes = List<int>.generate(16, (_) => _random.nextInt(256));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;

    final String hex = bytes
        .map((int byte) => byte.toRadixString(16).padLeft(2, '0'))
        .join();
    return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-'
        '${hex.substring(12, 16)}-${hex.substring(16, 20)}-'
        '${hex.substring(20, 32)}';
  }
}
