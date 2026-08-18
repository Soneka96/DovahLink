import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/hello_payloads.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

import '../harness_process.dart';

/// A valid 64-character hex-encoded developer token, matching the fixed value the `.NET`
/// integration suite uses (`integration/DovahLinkValidationClient.Tests/BridgeScenario.cs`).
const String _validHexToken =
    '0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344';

/// The documented Phase 1 loopback bridge endpoint (`bridge/README.md`'s "Default loopback port").
final Uri _bridgeUri = Uri.parse('ws://127.0.0.1:58231/');

/// Builds an isolated trust-store file path so this test never touches the developer's real
/// per-user trust store, mirroring the `.NET` pairing scenarios' own isolation.
String _isolatedTrustStorePath() =>
    '${Directory.systemTemp.path}${Platform.pathSeparator}'
    'dovahlink-trust-${DateTime.now().microsecondsSinceEpoch}.json';

void main() {
  group('WebSocketTransport', () {
    test(
      'connects to the real bridge harness and round-trips a raw hello/hello_ack',
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
        await transport.connect(_bridgeUri);

        final Envelope helloEnvelope = Envelope(
          messageType: 'hello',
          messageId: 'test-hello-1',
          sessionId: null,
          correlationId: null,
          payload: const HelloPayload(
            clientId: 'client-1',
            authMethod: AuthMethod.unpaired,
          ).toJson(),
          bridgeInstanceId: null,
          playContextId: null,
          clientId: null,
        );
        await transport.send(jsonEncode(helloEnvelope.toJson()));

        final String rawResponse = await transport.messages.first;
        final Envelope response = Envelope.fromJson(
          jsonDecode(rawResponse) as Map<String, dynamic>,
        );

        expect(response.messageType, 'hello_ack');
        expect(response.sessionId, isNotEmpty);
        expect(response.correlationId, 'test-hello-1');
      },
    );

    test('close() tears down the real socket without hanging', () async {
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
      await transport.connect(_bridgeUri);

      await transport.close().timeout(const Duration(seconds: 5));

      expect(() => transport.send('irrelevant'), throwsStateError);
    });

    test(
      'connect() to an unreachable port fails promptly rather than hanging',
      () async {
        final WebSocketTransport transport = WebSocketTransport();

        await expectLater(
          transport
              .connect(Uri.parse('ws://127.0.0.1:1/'))
              .timeout(const Duration(seconds: 5)),
          throwsA(isNot(isA<TimeoutException>())),
        );
      },
    );
  });
}
