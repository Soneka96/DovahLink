import 'dart:io';

import 'dovahlink_transport.dart';

/// A [DovahLinkTransport] backed by `dart:io`'s built-in [WebSocket] -- no additional package
/// dependency, sufficient for this desktop client.
class WebSocketTransport implements DovahLinkTransport {
  /// The underlying socket, once [connect] has succeeded.
  WebSocket? _socket;

  /// The decoded message stream, computed once per [connect] so every access of [messages]
  /// observes the same subscription rather than each racing its own listen against the socket.
  Stream<String>? _messages;

  @override
  Future<void> connect(Uri uri) async {
    if (_socket != null) {
      throw StateError(
        'Already connected. Call close() before connecting again.',
      );
    }
    final WebSocket socket = await WebSocket.connect(uri.toString());
    _socket = socket;
    // A non-text frame throws inside map(), which Dart delivers as a stream error event to
    // messages' listener, not a synchronous throw at the call site -- DovahLink only ever sends
    // text frames, so this is a protocol-violation signal, not an expected branch.
    _messages = socket.map((dynamic event) {
      if (event is! String) {
        throw StateError(
          'Received a non-text WebSocket frame; DovahLink only sends text frames.',
        );
      }
      return event;
    });
  }

  @override
  Future<void> send(String text) async {
    _requireSocket().add(text);
  }

  @override
  Stream<String> get messages {
    final Stream<String>? messages = _messages;
    if (messages == null) {
      throw StateError('Not connected. Call connect() first.');
    }
    return messages;
  }

  @override
  Future<void> close() async {
    final WebSocket? socket = _socket;
    _socket = null;
    _messages = null;
    await socket?.close();
  }

  /// Returns the connected socket, or throws if [connect] has not succeeded yet.
  WebSocket _requireSocket() {
    final WebSocket? socket = _socket;
    if (socket == null) {
      throw StateError('Not connected. Call connect() first.');
    }
    return socket;
  }
}
