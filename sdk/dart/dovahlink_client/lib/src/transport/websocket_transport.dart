import 'dart:io';

import 'package:async/async.dart';

import 'dovahlink_transport.dart';

/// A [DovahLinkTransport] backed by `dart:io`'s built-in [WebSocket] -- no additional package
/// dependency beyond `package:async`'s [StreamQueue], sufficient for this desktop client.
class WebSocketTransport implements DovahLinkTransport {
  /// The underlying socket, once [connect] has succeeded.
  WebSocket? _socket;

  /// Pulls one decoded text message at a time from the socket.
  ///
  /// `dart:io`'s [WebSocket] is a single-subscription stream: it can only ever be listened to
  /// once. [DovahLinkTransport.messages] is read repeatedly over a connection's lifetime (once
  /// per expected reply), so a cached `Stream.map()` over the raw socket would throw "Stream has
  /// already been listened to" on the second read -- confirmed by a real end-to-end test against
  /// the bridge, not by any fake-transport unit test, since only a real single-subscription
  /// stream exhibits this. [StreamQueue] listens exactly once and buffers, letting [messages]
  /// hand out one already-arrived (or not-yet-arrived) message per access instead.
  StreamQueue<String>? _messageQueue;

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
    // ponytail: connect() assumes no other connect() call is already in flight on this instance
    // -- true of every caller in this codebase today (DovahLinkClient always awaits one attempt
    // before starting another). A second overlapping connect() would race this reset against the
    // first call's own _abandoned check. Add a "connecting" guard if a caller ever needs to start
    // connect() again before a previous call has resolved or thrown.
    _abandoned = false;
    final WebSocket socket = await WebSocket.connect(uri.toString());
    if (_abandoned) {
      await socket.close();
      return;
    }
    _socket = socket;
    // A non-text frame throws inside map(), which the StreamQueue delivers as an error to
    // whichever `messages` access is waiting on it -- DovahLink only ever sends text frames, so
    // this is a protocol-violation signal, not an expected branch.
    _messageQueue = StreamQueue<String>(
      socket.map((dynamic event) {
        if (event is! String) {
          throw StateError(
            'Received a non-text WebSocket frame; DovahLink only sends text frames.',
          );
        }
        return event;
      }),
    );
  }

  @override
  Future<void> send(String text) async {
    _requireSocket().add(text);
  }

  @override
  Stream<String> get messages async* {
    final StreamQueue<String>? queue = _messageQueue;
    if (queue == null) {
      throw StateError('Not connected. Call connect() first.');
    }
    // An async* generator body does not run until its stream is actually listened to, so
    // queue.next -- which dequeues the next message the moment it runs -- only fires once a
    // caller subscribes, not merely on accessing this getter. A non-generator
    // `Stream.fromFuture(queue.next)` would call queue.next eagerly at getter-access time,
    // silently dequeuing a message even if the returned stream is never listened to.
    yield await queue.next;
  }

  @override
  Future<void> close() async {
    _abandoned = true;
    final WebSocket? socket = _socket;
    _socket = null;
    _messageQueue = null;
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
