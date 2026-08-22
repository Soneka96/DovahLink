import 'dart:math';

/// Generates cryptographically random, session-unique request message IDs.
class MessageIdGenerator {
  /// The secure random source used for message ID bytes.
  final Random _random = Random.secure();

  /// Generates a 32-character lowercase hexadecimal message ID.
  String generate() {
    final List<int> bytes = List<int>.generate(16, (_) => _random.nextInt(256));
    return bytes
        .map((int byte) => byte.toRadixString(16).padLeft(2, '0'))
        .join();
  }
}
