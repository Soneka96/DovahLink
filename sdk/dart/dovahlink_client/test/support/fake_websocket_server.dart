import 'dart:async';
import 'dart:io';

/// A real local WebSocket peer for [WebSocketTransport] tests that need an actual socket without
/// depending on the legacy Bridge harness -- generalizes the inline `HttpServer` +
/// `WebSocketTransformer` pattern this suite already used for the binary-frame test.
class FakeWebSocketServer {
  FakeWebSocketServer._(this._server, this._connections);

  final HttpServer _server;
  final StreamController<WebSocket> _connections;

  /// Binds an OS-assigned loopback port and starts accepting WebSocket upgrades.
  static Future<FakeWebSocketServer> start() async {
    final HttpServer server = await HttpServer.bind(
      InternetAddress.loopbackIPv4,
      0,
    );
    // Broadcast, not single-subscription: a single-subscription StreamController's close() future
    // never completes unless something actually listened to it first, which would hang close()
    // in every test that never inspects a connection.
    final StreamController<WebSocket> connections =
        StreamController<WebSocket>.broadcast();
    server.listen((HttpRequest request) async {
      final WebSocket socket = await WebSocketTransformer.upgrade(request);
      // A connection can finish upgrading after a test has already torn this server down (for
      // example the abrupt-disconnect test closes the server while a connect() is in flight) --
      // adding to an already-closed controller throws, so this guards that harmless race instead
      // of letting it fail the test.
      if (!connections.isClosed) {
        connections.add(socket);
      }
    });
    return FakeWebSocketServer._(server, connections);
  }

  /// The `ws://` endpoint this server is listening on.
  Uri get uri => Uri.parse('ws://127.0.0.1:${_server.port}/');

  /// Each accepted connection's server-side socket, in the order clients connect. A test must
  /// start listening (`.first`, `.take(n).toList()`) before triggering the connection it wants to
  /// observe -- this is a broadcast stream, so it does not replay events to a late listener.
  Stream<WebSocket> get connections => _connections.stream;

  /// Stops accepting connections and releases the port. Does not close sockets already handed out
  /// via [connections] -- callers that opened one manage its lifetime themselves.
  Future<void> close() async {
    await _connections.close();
    await _server.close(force: true);
  }
}
