/// The raw text-message transport a [DovahLinkClient] sends and receives encoded protocol
/// envelopes over. Deliberately narrow: framing, connection lifecycle, and message delivery only
/// -- no protocol knowledge. Implementations own how a connection is actually established (a real
/// WebSocket, or an in-memory double for tests).
abstract interface class DovahLinkTransport {
  /// Establishes the connection to [uri]. Must be called before [send] or [messages].
  Future<void> connect(Uri uri);

  /// Sends one complete text message.
  Future<void> send(String text);

  /// Emits one complete text message per received frame, in arrival order.
  Stream<String> get messages;

  /// Closes the connection. Idempotent.
  Future<void> close();
}
