import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/hello_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';
import '../harness_process.dart';

/// A valid 64-character hex-encoded developer token, matching the fixed value the `.NET`
/// integration suite uses (`integration/DovahLinkValidationClient.Tests/BridgeScenario.cs`).
const String _validHexToken =
    '0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344';

/// Bounds every wait on a real socket operation in this suite, so a hung connection or response
/// fails the test with a clear timeout instead of blocking the run indefinitely.
const Duration _socketTimeout = Duration(seconds: 5);

/// Builds an isolated trust-store file path so this test never touches the developer's real
/// per-user trust store, mirroring the `.NET` pairing scenarios' own isolation.
String _isolatedTrustStorePath() =>
    '${Directory.systemTemp.path}${Platform.pathSeparator}'
    'dovahlink-trust-${DateTime.now().microsecondsSinceEpoch}.json';

/// Runs WebSocket transport behavior tests.
void main() {
  group('Behavior transport connection lifecycle behaves correctly', () {
    test(
      'Behavior transport connection lifecycle connects to the real bridge harness and round-trips a raw hello/hello_ack',
      () async {
        final HarnessProcess harness = await HarnessProcess.start(
          token: _validHexToken,
          extraEnvironment: <String, String>{
            'DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE':
                _isolatedTrustStorePath(),
          },
        );
        addTearDown(harness.dispose);
        await harness.waitForReady();

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);
        await transport.connect(harness.bridgeUri).timeout(_socketTimeout);

        final Envelope helloEnvelope = Envelope(
          messageType: ProtocolMessageType.hello,
          messageId: 'test-hello-1',
          sessionId: null,
          correlationId: null,
          payload: HelloPayload(
            clientId: 'client-1',
            authMethod: AuthMethod.unpaired,
          ).toJson(),
          bridgeInstanceId: null,
          playContextId: null,
          clientId: null,
        );
        await transport.send(jsonEncode(helloEnvelope.toJson()));

        final String rawResponse = await transport.messages.first.timeout(
          _socketTimeout,
        );
        final Envelope response = Envelope.fromJson(
          jsonDecode(rawResponse) as Map<String, dynamic>,
        );

        expect(response.messageType, ProtocolMessageType.helloAck);
        expect(response.sessionId, isNotEmpty);
        expect(response.correlationId, 'test-hello-1');
      },
    );

    test(
      'Behavior transport connection lifecycle close() tears down the real socket without hanging',
      () async {
        final HarnessProcess harness = await HarnessProcess.start(
          token: _validHexToken,
          extraEnvironment: <String, String>{
            'DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE':
                _isolatedTrustStorePath(),
          },
        );
        addTearDown(harness.dispose);
        await harness.waitForReady();

        final WebSocketTransport transport = WebSocketTransport();
        await transport.connect(harness.bridgeUri).timeout(_socketTimeout);

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
        final HarnessProcess harness = await HarnessProcess.start(
          token: _validHexToken,
          extraEnvironment: <String, String>{
            'DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE':
                _isolatedTrustStorePath(),
          },
        );
        addTearDown(harness.dispose);
        await harness.waitForReady();

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);

        // Deliberately not awaited: close() must run while connect() is still resolving. The
        // timeout is attached now, not at the later await, so it bounds the whole wait.
        final Future<void> connectFuture = transport
            .connect(harness.bridgeUri)
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
        final HarnessProcess harness = await HarnessProcess.start(
          token: _validHexToken,
          extraEnvironment: <String, String>{
            'DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE':
                _isolatedTrustStorePath(),
          },
        );
        addTearDown(harness.dispose);
        await harness.waitForReady();

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);

        // Abandon a first in-flight connect(), then reconnect on the same instance -- this is
        // exactly what a real reconnect does (DovahLinkClient reuses one WebSocketTransport), so
        // a stale _abandoned flag must not sabotage it.
        final Future<void> firstConnect = transport
            .connect(harness.bridgeUri)
            .timeout(_socketTimeout);
        await transport.close();
        await firstConnect;

        // The bridge's connection slot is released once its own worker thread notices the closed
        // socket, not the instant this client's close() call returns -- a real, if usually brief,
        // delay. Retry briefly rather than requiring the first attempt to land, mirroring
        // dovahlink_bridge_harness_test.cpp's own "binding a real OS port can still lag a moment
        // behind that; retry briefly" pattern.
        const int maxAttempts = 20;
        for (int attempt = 1; ; attempt++) {
          try {
            await transport.connect(harness.bridgeUri).timeout(_socketTimeout);
            break;
          } on IOException {
            if (attempt >= maxAttempts) {
              rethrow;
            }
            await Future<void>.delayed(const Duration(milliseconds: 50));
          }
        }

        // Proves the new socket was actually adopted this time, not discarded like the first.
        await transport.send('probe');
      },
    );

    test('Behavior transport connection lifecycle delivers hello_ack then the bridge\'s own '
        'follow-up capabilities push', () async {
      final HarnessProcess harness = await HarnessProcess.start(
        token: _validHexToken,
        extraEnvironment: <String, String>{
          'DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE':
              _isolatedTrustStorePath(),
        },
      );
      addTearDown(harness.dispose);
      await harness.waitForReady();

      final WebSocketTransport transport = WebSocketTransport();
      addTearDown(transport.close);
      await transport.connect(harness.bridgeUri).timeout(_socketTimeout);

      final List<String> received = <String>[];
      final StreamSubscription<String> subscription = transport.messages.listen(
        received.add,
      );
      addTearDown(subscription.cancel);

      final Envelope helloEnvelope = Envelope(
        messageType: ProtocolMessageType.hello,
        messageId: 'test-hello-1',
        sessionId: null,
        correlationId: null,
        payload: HelloPayload(
          clientId: 'client-1',
          authMethod: AuthMethod.unpaired,
        ).toJson(),
        bridgeInstanceId: null,
        playContextId: null,
        clientId: null,
      );
      await transport.send(jsonEncode(helloEnvelope.toJson()));

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

      final Envelope first = Envelope.fromJson(
        jsonDecode(received[0]) as Map<String, dynamic>,
      );
      final Envelope second = Envelope.fromJson(
        jsonDecode(received[1]) as Map<String, dynamic>,
      );
      expect(first.messageType, ProtocolMessageType.helloAck);
      expect(second.messageType, ProtocolMessageType.capabilities);
    });

    test(
      'Behavior transport connection lifecycle reports stream completion when the bridge process ends',
      () async {
        final HarnessProcess harness = await HarnessProcess.start(
          token: _validHexToken,
          extraEnvironment: <String, String>{
            'DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE':
                _isolatedTrustStorePath(),
          },
        );
        await harness.waitForReady();

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);
        await transport.connect(harness.bridgeUri).timeout(_socketTimeout);

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

        // Ends the bridge process outright rather than a graceful close, dropping the socket out
        // from under this still-active subscription.
        await harness.dispose();

        await streamEnded.future.timeout(_socketTimeout);
      },
    );

    test('Behavior transport connection lifecycle reconnect installs a fresh inbound stream while the old one stays spent, '
        'single-subscription stream, never reused', () async {
      final HarnessProcess harness = await HarnessProcess.start(
        token: _validHexToken,
        extraEnvironment: <String, String>{
          'DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE':
              _isolatedTrustStorePath(),
        },
      );
      addTearDown(harness.dispose);
      await harness.waitForReady();

      final WebSocketTransport transport = WebSocketTransport();
      addTearDown(transport.close);
      await transport.connect(harness.bridgeUri).timeout(_socketTimeout);
      final Stream<String> firstMessages = transport.messages;
      // Listened to exactly once, matching how DovahLinkClient's single receiver behaves --
      // proves the *second* listen attempt below is rejected because this stream was already
      // listened to, not merely because it was never listened to at all.
      final StreamSubscription<String> firstSubscription = firstMessages.listen(
        (_) {},
      );
      await firstSubscription.cancel();

      await transport.close();

      const int maxAttempts = 20;
      for (int attempt = 1; ; attempt++) {
        try {
          await transport.connect(harness.bridgeUri).timeout(_socketTimeout);
          break;
        } on IOException {
          if (attempt >= maxAttempts) {
            rethrow;
          }
          await Future<void>.delayed(const Duration(milliseconds: 50));
        }
      }

      final Stream<String> secondMessages = transport.messages;
      expect(
        identical(firstMessages, secondMessages),
        isFalse,
        reason: 'reconnect must install a fresh stream, not reuse the old one',
      );

      // The new stream genuinely works: a fresh hello/hello_ack round trip over it.
      final List<String> received = <String>[];
      final StreamSubscription<String> secondSubscription = secondMessages
          .listen(received.add);
      addTearDown(secondSubscription.cancel);
      final Envelope helloEnvelope = Envelope(
        messageType: ProtocolMessageType.hello,
        messageId: 'test-hello-2',
        sessionId: null,
        correlationId: null,
        payload: HelloPayload(
          clientId: 'client-2',
          authMethod: AuthMethod.unpaired,
        ).toJson(),
        bridgeInstanceId: null,
        playContextId: null,
        clientId: null,
      );
      await transport.send(jsonEncode(helloEnvelope.toJson()));
      // The bridge also sends a follow-up capabilities push after hello_ack; this test only
      // needs to see the first message actually arrive over the fresh stream.
      await Future.doWhile(() async {
        if (received.isNotEmpty) {
          return false;
        }
        await Future<void>.delayed(const Duration(milliseconds: 20));
        return true;
      }).timeout(_socketTimeout);
      expect(
        Envelope.fromJson(
          jsonDecode(received.first) as Map<String, dynamic>,
        ).messageType,
        ProtocolMessageType.helloAck,
      );

      // The old, already-once-listened-to stream is spent: a second listen on it is rejected
      // by dart:io's single-subscription contract, never silently delivering new-connection
      // traffic to a stale listener.
      expect(() => firstMessages.listen((_) {}), throwsStateError);
    });

    test(
      'Behavior transport connection lifecycle rejects a binary frame as a non-text protocol violation',
      () async {
        final HttpServer server = await HttpServer.bind(
          InternetAddress.loopbackIPv4,
          0,
        );
        final List<WebSocket> sockets = <WebSocket>[];
        final StreamSubscription<HttpRequest> serverSubscription = server
            .listen((HttpRequest request) async {
              final WebSocket socket = await WebSocketTransformer.upgrade(
                request,
              );
              sockets.add(socket);
              socket.add(<int>[1, 2, 3]);
            });
        addTearDown(() async {
          await serverSubscription.cancel();
          for (final WebSocket socket in sockets) {
            await socket.close();
          }
          await server.close(force: true);
        });

        final WebSocketTransport transport = WebSocketTransport();
        addTearDown(transport.close);
        await transport.connect(Uri.parse('ws://127.0.0.1:${server.port}/'));

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
