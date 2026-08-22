import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';

/// Thrown when SDK-owned persisted state ([PersistedClientState]) exists but cannot be read as
/// valid: undecryptable, malformed, or written by an unsupported format version. Never thrown for
/// a simply missing store -- that is a valid empty state, per `ai/context/protocol/security.md`'s
/// "a missing file is a valid empty store." Fails closed: a caller must never treat this as "no
/// trust" and silently proceed, since that would be indistinguishable from an attacker-tampered
/// store.
class DovahLinkStorageException implements Exception {
  /// Creates a storage exception with a diagnostic [message].
  const DovahLinkStorageException(this.message);

  /// A diagnostic description of the storage failure. Never the persisted secret material itself;
  /// safe to log.
  final String message;

  /// Implements [Object.toString].
  @override
  String toString() => 'DovahLinkStorageException: $message';
}
