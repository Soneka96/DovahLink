import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_discovery_service.dart'
    show buildDovahLinkDiscoveryServiceForTesting;
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart'
    show ProtocolMessageType, TimeoutClass;
import 'fixtures/fixtures.dart';
import 'support/fake_websocket_server.dart';

/// Bounds local socket acceptance, protocol replies, and teardown in discovery tests.
const Duration _socketTimeout = Duration(seconds: 5);

/// Mocks consumer persistence to detect any access from isolated discovery.
class MockConsumerStorage extends Mock implements IClientStorage {}

/// Starts a discovery probe, reads its hello and optionally sends a synthetic `hello_ack`.
/// Returns futures for the probe result, captured request, and peer-observed socket closure.
Future<
  (Future<DovahLinkHost?>, Future<JsonMap>, Future<void>, Future<List<JsonMap>>)
>
startDiscoveryExchange({
  required FakeWebSocketServer server,
  required Uri endpoint,
  JsonMap? helloAckPayload,
  String? rawReply,
  bool reply = true,
  bool dropConnection = false,
  Map<TimeoutClass, Duration> timeoutDurations = kTimeoutClassDurations,
}) async {
  final Future<WebSocket> acceptedConnection = server.connections.first;
  final DovahLinkDiscoveryService service =
      buildDovahLinkDiscoveryServiceForTesting(
        endpoint: endpoint,
        timeoutDurations: timeoutDurations,
      );
  final Future<DovahLinkHost?> discovery = service.discoverLocalHost();
  final WebSocket socket = await acceptedConnection.timeout(_socketTimeout);
  final Completer<void> socketClosed = Completer<void>();
  final Completer<JsonMap> helloReceived = Completer<JsonMap>();
  final Completer<List<JsonMap>> peerMessages = Completer<List<JsonMap>>();
  final List<JsonMap> receivedMessages = <JsonMap>[];
  socket.listen(
    (Object? message) {
      if (message is String) {
        final JsonMap decoded = jsonDecode(message) as JsonMap;
        receivedMessages.add(decoded);
        if (!helloReceived.isCompleted) {
          helloReceived.complete(decoded);
        }
      }
    },
    onDone: () {
      if (!socketClosed.isCompleted) {
        socketClosed.complete();
      }
      if (!peerMessages.isCompleted) {
        peerMessages.complete(List<JsonMap>.unmodifiable(receivedMessages));
      }
    },
  );
  final Future<JsonMap> request = helloReceived.future.timeout(_socketTimeout);
  final JsonMap hello = await request;
  if (reply) {
    if (rawReply != null) {
      socket.add(rawReply);
    } else {
      final JsonMap payload = helloAckPayload ?? _validHelloAckPayload;
      socket.add(
        jsonEncode(
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.helloAck,
            messageId: 'host-hello-ack',
            sessionId: 'session-discovery',
            correlationId: hello['messageId'] as String,
            payload: payload,
            stateAuthorityId: 'authority-discovery',
            clientId: ((hello['payload'] as JsonMap)['clientId'] as String),
          ).toJson(),
        ),
      );
    }
  } else if (dropConnection) {
    await socket.close();
  }
  return (discovery, request, socketClosed.future, peerMessages.future);
}

/// A valid identity and compatible version for synthetic discovery handshakes.
const JsonMap _validHelloAckPayload = <String, Object?>{
  'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
  'hostName': 'GONCALO-DESKTOP',
  'hostVersion': '0.5.0',
  'clientIdentityKind': 'unpaired',
};

/// Creates a copy of the representative valid `hello_ack` payload with one field replaced.
JsonMap helloAckPayloadWith(String field, Object? value) => <String, Object?>{
  ..._validHelloAckPayload,
  field: value,
};

/// Runs one successful discovery handshake with the supplied mutable Host name.
/// @param server The local WebSocket test Host.
/// @param service The service probing that Host.
/// @param hostName The current computer name to report in `hello_ack`.
/// @return The discovered Host and a future completed when the probe socket closes.
Future<(DovahLinkHost?, Future<void>)> discoverHostWithName({
  required FakeWebSocketServer server,
  required DovahLinkDiscoveryService service,
  required String hostName,
}) async {
  final Future<WebSocket> accepted = server.connections.first;
  final Future<DovahLinkHost?> discovery = service.discoverLocalHost();
  final WebSocket socket = await accepted.timeout(_socketTimeout);
  final Completer<void> socketClosed = Completer<void>();
  final Completer<JsonMap> helloReceived = Completer<JsonMap>();
  socket.listen(
    (Object? message) {
      if (message is String && !helloReceived.isCompleted) {
        helloReceived.complete(jsonDecode(message) as JsonMap);
      }
    },
    onDone: () {
      if (!socketClosed.isCompleted) {
        socketClosed.complete();
      }
    },
  );
  final JsonMap hello = await helloReceived.future.timeout(_socketTimeout);
  socket.add(
    jsonEncode(
      Fixtures.buildEnvelope(
        messageType: ProtocolMessageType.helloAck,
        messageId: 'host-hello-ack',
        sessionId: 'session-discovery',
        correlationId: hello['messageId'] as String,
        payload: helloAckPayloadWith('hostName', hostName),
        stateAuthorityId: 'authority-discovery',
        clientId: (hello['payload'] as JsonMap)['clientId'] as String,
      ).toJson(),
    ),
  );
  return (await discovery.timeout(_socketTimeout), socketClosed.future);
}

void main() {
  setUpAll(() {
    registerFallbackValue(const PersistedClientState());
  });

  group('Method discoverLocalHost behaves correctly', () {
    test(
      'Method discoverLocalHost uses an unpaired one-shot probe and returns the Host claim',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);

        final (
          Future<DovahLinkHost?> discovery,
          Future<JsonMap> request,
          Future<void> socketClosed,
          Future<List<JsonMap>> peerMessages,
        ) = await startDiscoveryExchange(
          server: server,
          endpoint: server.uri,
        );
        final DovahLinkHost? host = await discovery.timeout(_socketTimeout);
        final JsonMap hello = await request;
        await socketClosed.timeout(_socketTimeout);
        final List<JsonMap> messages = await peerMessages.timeout(
          _socketTimeout,
        );

        expect(host?.hostId, _validHelloAckPayload['hostId']);
        expect(host?.hostName, _validHelloAckPayload['hostName']);
        expect(host?.endpoint, server.uri);
        expect((hello['payload'] as JsonMap)['auth'], <String, Object?>{
          'method': 'unpaired',
        });
        expect(
          ((hello['payload'] as JsonMap)['auth'] as JsonMap).containsKey(
            'token',
          ),
          isFalse,
        );
        expect(messages, hasLength(1));
        expect(messages.single['messageType'], 'hello');
      },
    );

    test(
      'Method discoverLocalHost leaves consumer credential and Known Host storage untouched',
      () async {
        final DovahLinkHost knownHost = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'KNOWN-HOST',
          endpoint: Uri.parse('ws://127.0.0.1:58230/'),
        );
        final PersistedClientState consumerState = PersistedClientState(
          clientId: 'client-1',
          credential: 'private-credential',
          recoveryState: PairingRecoveryState.confirming,
          knownHost: knownHost,
        );
        final MockConsumerStorage consumerStorage = MockConsumerStorage();
        int loadCount = 0;
        int saveCount = 0;
        int clearCount = 0;
        when(() => consumerStorage.load()).thenAnswer((_) async {
          loadCount++;
          return consumerState;
        });
        when(() => consumerStorage.save(any())).thenAnswer((_) async {
          saveCount++;
        });
        when(() => consumerStorage.clear()).thenAnswer((_) async {
          clearCount++;
        });
        final DovahLinkClient consumer = DovahLinkClient(
          storage: consumerStorage,
        );
        expect(await consumer.loadKnownHost(), knownHost);
        final int loadsBeforeDiscovery = loadCount;

        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);
        final (
          Future<DovahLinkHost?> discovery,
          Future<JsonMap> request,
          Future<void> socketClosed,
          Future<List<JsonMap>> peerMessages,
        ) = await startDiscoveryExchange(
          server: server,
          endpoint: server.uri,
          helloAckPayload: helloAckPayloadWith(
            'hostId',
            'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          ),
        );
        final DovahLinkHost? candidate = await discovery;
        final JsonMap hello = await request;
        await socketClosed.timeout(_socketTimeout);
        final List<JsonMap> messages = await peerMessages.timeout(
          _socketTimeout,
        );

        expect(candidate?.hostId, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
        expect(
          ((hello['payload'] as JsonMap)['auth'] as JsonMap),
          <String, Object?>{'method': 'unpaired'},
        );
        expect(messages, hasLength(1));
        expect(loadCount, loadsBeforeDiscovery);
        expect(saveCount, 0);
        expect(clearCount, 0);
        expect(await consumer.loadKnownHost(), knownHost);
        expect(loadCount, loadsBeforeDiscovery + 1);
        expect(consumerState.credential, 'private-credential');
        expect(consumerState.recoveryState, PairingRecoveryState.confirming);
      },
    );

    test(
      'Method discoverLocalHost returns the same values for repeated discovery',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);

        final (
          Future<DovahLinkHost?> firstDiscovery,
          Future<JsonMap> firstRequest,
          Future<void> firstClosed,
          _,
        ) = await startDiscoveryExchange(
          server: server,
          endpoint: server.uri,
        );
        final DovahLinkHost? firstHost = await firstDiscovery;
        await firstRequest;
        await firstClosed.timeout(_socketTimeout);

        final (
          Future<DovahLinkHost?> secondDiscovery,
          Future<JsonMap> secondRequest,
          Future<void> secondClosed,
          _,
        ) = await startDiscoveryExchange(
          server: server,
          endpoint: server.uri,
        );
        final DovahLinkHost? secondHost = await secondDiscovery;
        await secondRequest;
        await secondClosed.timeout(_socketTimeout);

        expect(secondHost?.hostId, firstHost?.hostId);
        expect(secondHost?.hostName, firstHost?.hostName);
        expect(secondHost?.endpoint, firstHost?.endpoint);
      },
    );

    test(
      'Method discoverLocalHost returns null when the candidate has no listener',
      () async {
        final DovahLinkDiscoveryService service =
            buildDovahLinkDiscoveryServiceForTesting(
              endpoint: Uri.parse('ws://127.0.0.1:1/'),
            );

        expect(await service.discoverLocalHost(), isNull);
      },
    );

    test(
      'Method discoverLocalHost throws with the HTTP status from a reachable non-WebSocket service',
      () async {
        final HttpServer server = await HttpServer.bind(
          InternetAddress.loopbackIPv4,
          0,
        );
        addTearDown(() => server.close(force: true));
        final Completer<void> requestReceived = Completer<void>();
        server.listen((HttpRequest request) async {
          if (!requestReceived.isCompleted) {
            requestReceived.complete();
          }
          request.response.statusCode = HttpStatus.ok;
          await request.response.close();
        });
        final DovahLinkDiscoveryService service =
            buildDovahLinkDiscoveryServiceForTesting(
              endpoint: Uri.parse('ws://127.0.0.1:${server.port}/'),
            );

        await expectLater(
          service.discoverLocalHost(),
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException error) => error.httpStatusCode,
              'httpStatusCode',
              HttpStatus.ok,
            ),
          ),
        );
        await requestReceived.future.timeout(_socketTimeout);
      },
    );

    test(
      'Method discoverLocalHost preserves Host identity across different endpoints',
      () async {
        final FakeWebSocketServer firstServer =
            await FakeWebSocketServer.start();
        final FakeWebSocketServer secondServer =
            await FakeWebSocketServer.start();
        addTearDown(firstServer.close);
        addTearDown(secondServer.close);

        final (
          Future<DovahLinkHost?> firstDiscovery,
          Future<JsonMap> firstRequest,
          Future<void> firstClosed,
          _,
        ) = await startDiscoveryExchange(
          server: firstServer,
          endpoint: firstServer.uri,
        );
        final DovahLinkHost? firstHost = await firstDiscovery;
        await firstRequest;
        await firstClosed.timeout(_socketTimeout);

        final (
          Future<DovahLinkHost?> secondDiscovery,
          Future<JsonMap> secondRequest,
          Future<void> secondClosed,
          _,
        ) = await startDiscoveryExchange(
          server: secondServer,
          endpoint: secondServer.uri,
        );
        final DovahLinkHost? secondHost = await secondDiscovery;
        await secondRequest;
        await secondClosed.timeout(_socketTimeout);

        expect(firstHost?.hostId, secondHost?.hostId);
        expect(firstHost?.endpoint, isNot(secondHost?.endpoint));
      },
    );

    test(
      'Method discoverLocalHost treats a changed computer name as metadata',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);
        final Uri endpoint = server.uri;
        final DovahLinkDiscoveryService service =
            buildDovahLinkDiscoveryServiceForTesting(endpoint: endpoint);

        final (
          DovahLinkHost? oldHost,
          Future<void> oldClosed,
        ) = await discoverHostWithName(
          server: server,
          service: service,
          hostName: 'OLD-DESKTOP',
        );
        await oldClosed.timeout(_socketTimeout);
        final (
          DovahLinkHost? newHost,
          Future<void> newClosed,
        ) = await discoverHostWithName(
          server: server,
          service: service,
          hostName: 'NEW-DESKTOP',
        );
        await newClosed.timeout(_socketTimeout);

        expect(oldHost?.hostId, newHost?.hostId);
        expect(oldHost?.hostName, 'OLD-DESKTOP');
        expect(newHost?.hostName, 'NEW-DESKTOP');
      },
    );

    test(
      'Method discoverLocalHost preserves malformed responder and Host identity failures',
      () async {
        for (final MapEntry<String, Object?> invalidField
            in <MapEntry<String, Object?>>[
              const MapEntry<String, Object?>('hostId', 'not-a-uuid'),
              const MapEntry<String, Object?>('hostName', ' '),
            ]) {
          final FakeWebSocketServer server = await FakeWebSocketServer.start();
          addTearDown(server.close);
          final (
            Future<DovahLinkHost?> discovery,
            Future<JsonMap> request,
            Future<void> socketClosed,
            _,
          ) = await startDiscoveryExchange(
            server: server,
            endpoint: server.uri,
            helloAckPayload: helloAckPayloadWith(
              invalidField.key,
              invalidField.value,
            ),
          );

          await expectLater(
            discovery,
            throwsA(isA<DovahLinkProtocolException>()),
          );
          await request;
          await socketClosed.timeout(_socketTimeout);
        }

        final FakeWebSocketServer malformedServer =
            await FakeWebSocketServer.start();
        addTearDown(malformedServer.close);
        final (
          Future<DovahLinkHost?> malformedDiscovery,
          Future<JsonMap> malformedRequest,
          Future<void> malformedClosed,
          _,
        ) = await startDiscoveryExchange(
          server: malformedServer,
          endpoint: malformedServer.uri,
          rawReply: '{not-json',
        );
        await expectLater(
          malformedDiscovery,
          throwsA(isA<DovahLinkProtocolException>()),
        );
        await malformedRequest;
        await malformedClosed.timeout(_socketTimeout);
      },
    );

    test(
      'Method discoverLocalHost preserves an incompatible Host failure and closes the probe',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);
        final (
          Future<DovahLinkHost?> discovery,
          Future<JsonMap> request,
          Future<void> socketClosed,
          _,
        ) = await startDiscoveryExchange(
          server: server,
          endpoint: server.uri,
          helloAckPayload: helloAckPayloadWith('hostVersion', '0.4.0'),
        );

        await expectLater(
          discovery,
          throwsA(isA<DovahLinkCompatibilityException>()),
        );
        await request;
        await socketClosed.timeout(_socketTimeout);
      },
    );

    test(
      'Method discoverLocalHost times out a silent peer and closes the probe',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);
        final (
          Future<DovahLinkHost?> discovery,
          Future<JsonMap> request,
          Future<void> socketClosed,
          _,
        ) = await startDiscoveryExchange(
          server: server,
          endpoint: server.uri,
          reply: false,
          timeoutDurations: <TimeoutClass, Duration>{
            TimeoutClass.short: const Duration(milliseconds: 25),
            TimeoutClass.normal: const Duration(milliseconds: 25),
            TimeoutClass.heavy: const Duration(milliseconds: 25),
          },
        );

        await expectLater(
          discovery,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await request;
        await socketClosed.timeout(_socketTimeout);
      },
    );

    test(
      'Method discoverLocalHost closes a dropped probe without reconnecting',
      () async {
        final FakeWebSocketServer server = await FakeWebSocketServer.start();
        addTearDown(server.close);
        int acceptedConnectionCount = 0;
        final StreamSubscription<WebSocket> connectionObserver = server
            .connections
            .listen((WebSocket _) => acceptedConnectionCount++);
        addTearDown(connectionObserver.cancel);
        final (
          Future<DovahLinkHost?> discovery,
          Future<JsonMap> request,
          Future<void> socketClosed,
          _,
        ) = await startDiscoveryExchange(
          server: server,
          endpoint: server.uri,
          reply: false,
          dropConnection: true,
        );

        await expectLater(
          discovery,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await request;
        await socketClosed.timeout(_socketTimeout);
        await Future<void>.delayed(const Duration(milliseconds: 100));

        expect(acceptedConnectionCount, 1);
      },
    );
  });
}
