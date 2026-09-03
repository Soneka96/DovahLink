@Tags(<String>['legacy_bridge'])
library;

import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/hello_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';
import '../fixtures/fixtures.dart';
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

/// Legacy Bridge compatibility coverage: proves [WebSocketTransport] round-trips real Bridge wire
/// semantics against a live `dovahlink_bridge_harness` process, per `ARCHITECTURE.md`'s "Bridge
/// migration and cutover" and `ai/context/sdk/testing.md`'s "Transport fidelity". Tagged
/// `legacy_bridge` and skipped by default (see `dart_test.yaml`) -- not part of default SDK/App
/// CI. Run explicitly with `dart test --tags legacy_bridge --run-skipped` after building the
/// harness (`cmake --build --preset windows-x64-debug --target dovahlink_bridge_harness` in
/// `bridge/`). Transport-only mechanics (framing, connection lifecycle, reconnect) that do not
/// depend on Bridge's own protocol behavior live in `websocket_transport_test.dart` instead.
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

        final Envelope helloEnvelope = Fixtures.buildEnvelope(
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
      'Behavior transport connection lifecycle delivers hello_ack then the bridge\'s own '
      'follow-up capabilities push',
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

        final List<String> received = <String>[];
        final StreamSubscription<String> subscription = transport.messages
            .listen(received.add);
        addTearDown(subscription.cancel);

        final Envelope helloEnvelope = Fixtures.buildEnvelope(
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
      },
    );
  });
}
