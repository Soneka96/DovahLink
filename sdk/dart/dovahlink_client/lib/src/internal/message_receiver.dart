/// Ensures the transport's inbound message stream is being read, for a collaborator that sends a
/// request before the connection is otherwise guaranteed to be receiving -- see
/// `ai/context/sdk/architecture.md`'s "Internal composition". Implemented by [ClientSession].
abstract interface class MessageReceiver {
  /// Ensures exactly one subscription is reading the current connection's inbound message stream.
  /// Idempotent: a no-op once a subscription already exists.
  void ensureReceiving();
}
