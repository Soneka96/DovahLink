import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/dovahlink_client.dart';

import 'harness_process.dart';

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

/// Reads and validates the harness's `PAIRING_CODE <code>` report line, printed by
/// `StdoutPairingNotificationSink` the moment a fresh pairing challenge starts.
Future<String> _readPairingCode(HarnessProcess harness) async {
  const String prefix = 'PAIRING_CODE ';
  final String line = await harness.readLine();
  if (!line.startsWith(prefix)) {
    throw StateError(
      'Harness did not report a pairing code: $line. Stderr: ${harness.stderrOutput}',
    );
  }
  return line.substring(prefix.length);
}

/// Starts an isolated harness and a connected, unpaired `DovahLinkClient` session, ready for a
/// pairing scenario.
///
/// Registers teardown for both the harness and the client immediately after each is created,
/// before anything that can throw -- per `HarnessProcess.start`'s own documented contract -- so a
/// failure partway through this setup (e.g. `hello` rejecting) still cleans up rather than leaking
/// the process or socket.
Future<(HarnessProcess, DovahLinkClient)> _startUnpairedSession() async {
  final HarnessProcess harness = await HarnessProcess.start(
    token: _validHexToken,
    extraEnvironment: <String, String>{
      'DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE': _isolatedTrustStorePath(),
    },
  );
  addTearDown(harness.dispose);
  await harness.waitForReady();

  final DovahLinkClient client = DovahLinkClient();
  addTearDown(client.disconnect);
  await client.connect(_bridgeUri);
  final HelloResult result = await client.hello(clientId: 'client-1');
  expect(result.trustState, DovahLinkTrustState.unpaired);

  return (harness, client);
}

void main() {
  group('DovahLinkClient real end-to-end pairing', () {
    test(
      'a full pairing round trip reaches trusted, and the credential survives a reconnect',
      () async {
        final (HarnessProcess harness, DovahLinkClient client) =
            await _startUnpairedSession();

        final PairingAvailability availability = await client.requestPairing();
        expect(availability, PairingAvailability.available);

        final String code = await _readPairingCode(harness);
        final String credential = await client.confirmPairingCode(
          code: code,
          displayName: 'Integration Test',
        );

        await client.acknowledgeTrustedCredential(credential);
        expect(client.trustState, DovahLinkTrustState.trusted);

        // The issued credential must actually work on its own, not just as an in-memory upgrade
        // of this connection's session: disconnect, then reconnect with trusted_device_credential
        // using the credential, proving the bridge's trust store actually persisted it.
        await client.disconnect();
        final DovahLinkClient reconnected = DovahLinkClient();
        addTearDown(reconnected.disconnect);
        await reconnected.connect(_bridgeUri);
        final HelloResult reconnectResult = await reconnected.hello(
          clientId: 'client-1',
          credential: credential,
        );

        expect(reconnectResult.trustState, DovahLinkTrustState.trusted);
      },
    );

    test(
      'an incorrect code yields DovahLinkPairingException(invalid), and the session stays unpaired',
      () async {
        final (HarnessProcess harness, DovahLinkClient client) =
            await _startUnpairedSession();

        final PairingAvailability availability = await client.requestPairing();
        expect(availability, PairingAvailability.available);

        final String realCode = await _readPairingCode(harness);
        final String wrongDigit = realCode[realCode.length - 1] == '0'
            ? '1'
            : '0';
        final String wrongCode =
            '${realCode.substring(0, realCode.length - 1)}$wrongDigit';

        await expectLater(
          client.confirmPairingCode(code: wrongCode),
          throwsA(
            isA<DovahLinkPairingException>().having(
              (DovahLinkPairingException e) => e.outcome,
              'outcome',
              'invalid',
            ),
          ),
        );
        expect(client.trustState, DovahLinkTrustState.unpaired);
      },
    );
  });
}
