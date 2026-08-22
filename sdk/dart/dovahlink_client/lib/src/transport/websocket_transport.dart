import 'dart:io';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// A [DovahLinkTransport] backed by `dart:io`'s built-in [WebSocket] -- no additional package
/// dependency needed for this desktop client.
class WebSocketTransport implements DovahLinkTransport {
  /// The underlying socket, once [connect] has succeeded.
  WebSocket? _socket;

  /// One continuous, ordered stream of decoded text frames for the lifetime of [_socket], cached
  /// so every [messages] access returns the same reference. `dart:io`'s [WebSocket] is a
  /// single-subscription stream -- it can only ever be listened to once in its entire lifetime,
  /// cancelled or not -- so [DovahLinkClient] must subscribe exactly once per connection and keep
  /// that one subscription for as long as the connection lives; a second [messages] listen throws
  /// "Stream has already been listened to", which is the desired protection against a second
  /// reader ever attaching to the same socket.
  Stream<String>? _messages;

  /// Set by [close] to discard a still-resolving [connect] instead of adopting its socket.
  /// `dart:io`'s [WebSocket.connect] has no cancellation: a caller that gives up on [connect] (for
  /// example via its own `.timeout()`) does not stop it from eventually succeeding in the
  /// background, and without this flag that late socket would silently end up open with nothing
  /// managing it. Reset at the start of every [connect] call, since one transport instance is
  /// reused across reconnects.
  bool _abandoned = false;

  @override
  Future<void> connect(Uri uri) async {
    if (_socket != null) {
      throw StateError(
        'Already connected. Call close() before connecting again.',
      );
    }
    // Known limitation: connect() assumes no other connect() call is already in flight on this
    // instance -- true of every caller in this codebase today (DovahLinkClient always awaits one
    // attempt before starting another). A second overlapping connect() would race this reset
    // against the first call's own _abandoned check. Add a "connecting" guard if a caller ever
    // needs to start connect() again before a previous call has resolved or thrown.
    _abandoned = false;
    final WebSocket socket = await WebSocket.connect(uri.toString());
    if (_abandoned) {
      await socket.close();
      return;
    }
    _socket = socket;
    // A non-text frame throws inside map(), which the stream delivers as an error to whoever is
    // listening -- DovahLink only ever sends text frames, so this is a protocol-violation signal,
    // not an expected branch.
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
    _abandoned = true;
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
