import 'dart:async';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';
import '../support/fake_websocket_server.dart';

/// Bounds every wait on a real socket operation in this suite, so a hung connection or response
/// fails the test with a clear timeout instead of blocking the run indefinitely.
const Duration _socketTimeout = Duration(seconds: 5);

/// Runs WebSocket transport behavior tests against a real local socket. Deliberately
/// protocol-agnostic: these prove [WebSocketTransport]'s own connection-lifecycle and framing
/// mechanics against a [FakeWebSocketServer], not any peer's protocol semantics -- Bridge-specific
/// wire compatibility lives in `websocket_transport_bridge_test.dart` instead.
void main() {
  group('Behavior transport connection lifecycle behaves correctly', () {
    test(
      'Behavior transport connection lifecycle close() tears down the real socket without hanging',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);

        final WebSocketTransport transport = WebSocketTransport();
        await transport.connect(server.uri).timeout(_socketTimeout);

        await transport.close().timeout(_socketTimeout);

        expect(() => transport.send('irrelevant'), throwsStateError);
      },
    );

    test(
      'Behavior transport connection lifecycle connect() to an unreachable port fails promptly rather than hanging',
      () async {
        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);

        await expectLater(
          transport
              .connect(Uri.parse('ws://127.0.0.1:1/'))
              .timeout(_socketTimeout),
          throwsA(isA<SocketException>()),
        );
      },
    );

    test(
      'Behavior transport connection lifecycle close() during an in-flight connect() discards the late socket instead of adopting it',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);

        // Deliberately not awaited: close() must run while connect() is still resolving. The
        // timeout is attached now, not at the later await, so it bounds the whole wait.
        final Future<void> connectFuture = transport
            .connect(server.uri)
            .timeout(_socketTimeout);
        await transport.close();
        await connectFuture;

        // The socket connect() eventually received was discarded, not adopted -- the
        // transport still behaves as never connected.
        expect(() => transport.send('irrelevant'), throwsStateError);
      },
    );

    test(
      'Behavior transport connection lifecycle reconnects successfully after an abandoned connect()',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);

        // Abandon a first in-flight connect(), then reconnect on the same instance -- this is
        // exactly what a real reconnect does (DovahLinkClient reuses one WebSocketTransport), so
        // a stale _abandoned flag must not sabotage it.
        final Future<void> firstConnect = transport
            .connect(server.uri)
            .timeout(_socketTimeout);
        await transport.close();
        await firstConnect;

        // A fake local server accepts immediately, unlike the real Bridge (whose connection slot
        // is only released once its own worker thread notices the closed socket) -- so, unlike
        // that real-peer scenario, no retry-until-accepted loop is needed here.
        await transport.connect(server.uri).timeout(_socketTimeout);

        // Proves the new socket was actually adopted this time, not discarded like the first.
        await transport.send('probe');
      },
    );

    test(
      'Behavior transport connection lifecycle delivers multiple messages over one continuous ordered stream',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);
        final Future<WebSocket> accepted = server.connections.first;

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);
        await transport.connect(server.uri).timeout(_socketTimeout);
        final WebSocket serverSocket = await accepted.timeout(_socketTimeout);

        final List<String> received = <String>[];
        final StreamSubscription<String> subscription = transport.messages
            .listen(received.add);
        addTearDown(subscription.cancel);

        serverSocket.add('first');
        serverSocket.add('second');

        // Both messages arrive on this one subscription -- proves the transport's stream is a
        // genuine continuous, ordered stream for the connection's lifetime, not a one-shot per
        // access.
        await Future.doWhile(() async {
          if (received.length >= 2) {
            return false;
          }
          await Future<void>.delayed(const Duration(milliseconds: 20));
          return true;
        }).timeout(_socketTimeout);

        expect(received, <String>['first', 'second']);
      },
    );

    test(
      'Behavior transport connection lifecycle reports stream completion when the peer closes the connection',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);
        final Future<WebSocket> accepted = server.connections.first;

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);
        await transport.connect(server.uri).timeout(_socketTimeout);
        final WebSocket serverSocket = await accepted.timeout(_socketTimeout);

        final Completer<void> streamEnded = Completer<void>();
        final StreamSubscription<String> subscription = transport.messages
            .listen(
              (_) {},
              onError: (Object _) {
                if (!streamEnded.isCompleted) {
                  streamEnded.complete();
                }
              },
              onDone: () {
                if (!streamEnded.isCompleted) {
                  streamEnded.complete();
                }
              },
            );
        addTearDown(subscription.cancel);

        // Closes the peer's own socket outright, dropping the connection out from under this
        // still-active subscription -- a plain HttpServer force-close does not touch a socket
        // already handed off via WebSocketTransformer.upgrade, so the accepted socket itself
        // must be closed to actually end the connection.
        await serverSocket.close();

        await streamEnded.future.timeout(_socketTimeout);
      },
    );

    test('Behavior transport connection lifecycle reconnect installs a fresh inbound stream while the old one stays spent, '
        'single-subscription stream, never reused', () async {
      final FakeWebSocketServer server = await FakeWebSocketServer.start();
      addTearDown(server.close);
      final Future<List<WebSocket>> accepted = server.connections
          .take(2)
          .toList();

      final WebSocketTransport transport = WebSocketTransport();
      addTearDown(transport.close);
      await transport.connect(server.uri).timeout(_socketTimeout);
      final Stream<String> firstMessages = transport.messages;
      // Listened to exactly once, matching how DovahLinkClient's single receiver behaves --
      // proves the *second* listen attempt below is rejected because this stream was already
      // listened to, not merely because it was never listened to at all.
      final StreamSubscription<String> firstSubscription = firstMessages.listen(
        (_) {},
      );
      await firstSubscription.cancel();

      await transport.close();
      await transport.connect(server.uri).timeout(_socketTimeout);

      final Stream<String> secondMessages = transport.messages;
      expect(
        identical(firstMessages, secondMessages),
        isFalse,
        reason: 'reconnect must install a fresh stream, not reuse the old one',
      );

      // The new stream genuinely works: a message the peer sends over the fresh connection
      // arrives.
      final List<WebSocket> serverSockets = await accepted.timeout(
        _socketTimeout,
      );
      final List<String> received = <String>[];
      final StreamSubscription<String> secondSubscription = secondMessages
          .listen(received.add);
      addTearDown(secondSubscription.cancel);
      serverSockets[1].add('probe-response');
      await Future.doWhile(() async {
        if (received.isNotEmpty) {
          return false;
        }
        await Future<void>.delayed(const Duration(milliseconds: 20));
        return true;
      }).timeout(_socketTimeout);
      expect(received.first, 'probe-response');

      // The old, already-once-listened-to stream is spent: a second listen on it is rejected
      // by dart:io's single-subscription contract, never silently delivering new-connection
      // traffic to a stale listener.
      expect(() => firstMessages.listen((_) {}), throwsStateError);
    });

    test(
      'Behavior transport connection lifecycle rejects a binary frame as a non-text protocol violation',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);
        final Future<WebSocket> accepted = server.connections.first;

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);
        await transport.connect(server.uri).timeout(_socketTimeout);
        (await accepted.timeout(_socketTimeout)).add(<int>[1, 2, 3]);

        final Completer<Object> streamError = Completer<Object>();
        final StreamSubscription<String> subscription = transport.messages
            .listen(
              (_) {},
              onError: (Object error) {
                if (!streamError.isCompleted) {
                  streamError.complete(error);
                }
              },
            );
        addTearDown(subscription.cancel);

        await expectLater(
          streamError.future.timeout(_socketTimeout),
          completion(isA<StateError>()),
        );
      },
    );
  });
}
