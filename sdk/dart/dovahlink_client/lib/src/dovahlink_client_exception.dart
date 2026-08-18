/// Thrown when establishing or maintaining the transport connection fails -- a socket-level
/// problem, not a bridge-reported failure.
class DovahLinkConnectionException implements Exception {
  /// Creates a connection exception with a diagnostic [message].
  const DovahLinkConnectionException(this.message);

  /// A diagnostic description of the connection failure. Never a raw exception or infrastructure
  /// detail; safe to log.
  final String message;

  @override
  String toString() => 'DovahLinkConnectionException: $message';
}

/// Thrown when the bridge reports a wire-level `error` message, or replies with an unexpected
/// message type.
class DovahLinkProtocolException implements Exception {
  /// Creates a protocol exception from the bridge's own reported failure fields, or a
  /// client-synthesized equivalent for an unexpected reply.
  const DovahLinkProtocolException({
    required this.code,
    required this.message,
    required this.retryable,
  });

  /// The canonical machine-readable failure code. For branching; never [message].
  final String code;

  /// Diagnostic text. Never used for branching.
  final String message;

  /// Whether a fresh connection may retry.
  final bool retryable;

  @override
  String toString() =>
      'DovahLinkProtocolException($code, retryable: $retryable): $message';
}

/// Thrown when a pairing operation reports a non-success outcome.
class DovahLinkPairingException implements Exception {
  /// Creates a pairing exception from the bridge's reported [outcome].
  const DovahLinkPairingException(this.outcome);

  /// The raw wire outcome: `expired`, `invalid`, `pacing_limited`, or `hard_limit_reached` (from
  /// `pairing_confirm`), `pending_not_found` (from `pairing_ack`).
  final String outcome;

  @override
  String toString() => 'DovahLinkPairingException: $outcome';
}

/// Thrown when SDK-owned persisted state ([PersistedClientState]) exists but cannot be read as
/// valid: undecryptable, malformed, or written by an unsupported format version. Never thrown for
/// a simply missing store -- that is a valid empty state, per
/// `ai/context/protocol/security.md`'s "a missing file is a valid empty store, not corruption."
/// Fails closed: a caller must never treat this as "no trust" and silently proceed, since that
/// would be indistinguishable from an attacker-tampered store.
class DovahLinkStorageException implements Exception {
  /// Creates a storage exception with a diagnostic [message].
  const DovahLinkStorageException(this.message);

  /// A diagnostic description of the storage failure. Never the persisted secret material itself;
  /// safe to log.
  final String message;

  @override
  String toString() => 'DovahLinkStorageException: $message';
}
