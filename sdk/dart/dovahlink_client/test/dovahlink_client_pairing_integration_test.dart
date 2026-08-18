import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';

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

/// Starts an isolated harness, ready for a pairing scenario against it.
Future<HarnessProcess> _startHarness() async {
  final HarnessProcess harness = await HarnessProcess.start(
    token: _validHexToken,
    extraEnvironment: <String, String>{
      'DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE': _isolatedTrustStorePath(),
    },
  );
  addTearDown(harness.dispose);
  await harness.waitForReady();
  return harness;
}

/// Connects and hellos a fresh, unpaired `DovahLinkClient` backed by [storage] -- an
/// `InMemoryClientStorage` shared across every client constructed for the same simulated
/// installation, so a later "relaunch" (a new `DovahLinkClient` instance reusing the same
/// [storage]) resolves the same persisted `clientId` and credential a real app restart would.
///
/// Registers teardown for the client immediately after construction, before anything that can
/// throw, so a failure partway through this setup (e.g. `hello` rejecting) still cleans up rather
/// than leaking the socket.
Future<DovahLinkClient> _startUnpairedSession(
  ClientStorage storage,
) async {
  final DovahLinkClient client = DovahLinkClient(storage: storage);
  addTearDown(client.disconnect);
  await client.connect(_bridgeUri);
  final HelloResult result = await client.hello();
  expect(result.trustState, DovahLinkTrustState.unpaired);
  return client;
}

void main() {
  group('DovahLinkClient real end-to-end pairing', () {
    test(
      'a full pairing round trip reaches trusted, and the credential survives a reconnect',
      () async {
        final HarnessProcess harness = await _startHarness();
        final ClientStorage storage = InMemoryClientStorage();
        final DovahLinkClient client = await _startUnpairedSession(storage);

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
        // of this connection's session: disconnect, then reconnect a fresh DovahLinkClient sharing
        // the same storage (simulating an app restart on the same installation), proving both the
        // bridge's trust store and this SDK's own persistence actually kept it.
        await client.disconnect();
        final DovahLinkClient reconnected = DovahLinkClient(storage: storage);
        addTearDown(reconnected.disconnect);
        await reconnected.connect(_bridgeUri);
        final HelloResult reconnectResult = await reconnected.hello();

        expect(reconnectResult.trustState, DovahLinkTrustState.trusted);
      },
    );

    test(
      'an incorrect code yields DovahLinkPairingException(invalid), and the session stays unpaired',
      () async {
        final HarnessProcess harness = await _startHarness();
        final DovahLinkClient client = await _startUnpairedSession(
          InMemoryClientStorage(),
        );

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

    test(
      'a client that disconnects after confirmPairingCode recovers to trusted on relaunch',
      () async {
        // This disconnects cleanly rather than simulating an abrupt crash (killing the raw socket
        // below the WebSocket handshake, which this transport does not expose) -- crash-vs-clean-
        // disconnect bridge-side connection-liveness behavior is Phase 3's own separate acceptance
        // concern, not this SDK-persistence recovery scenario.
        final HarnessProcess harness = await _startHarness();
        final ClientStorage storage = InMemoryClientStorage();
        final DovahLinkClient client = await _startUnpairedSession(storage);

        final PairingAvailability availability = await client.requestPairing();
        expect(availability, PairingAvailability.available);

        final String code = await _readPairingCode(harness);
        // Durably persists the credential with CONFIRMING recovery, per
        // `ai/context/protocol/security.md`. Deliberately never calls
        // acknowledgeTrustedCredential -- this leaves the gap between issuing the credential and
        // confirming it that `recoverPendingPairing` exists to close.
        await client.confirmPairingCode(code: code, displayName: 'Relaunch Test');
        await client.disconnect();

        // A new DovahLinkClient instance sharing the same storage simulates the app relaunching
        // on the same installation. It must hello as unpaired -- the bridge has not committed the
        // pending credential as trusted yet -- then recover the interrupted confirmation.
        final DovahLinkClient relaunched = DovahLinkClient(storage: storage);
        addTearDown(relaunched.disconnect);
        await relaunched.connect(_bridgeUri);
        final HelloResult helloResult = await relaunched.hello();
        expect(helloResult.trustState, DovahLinkTrustState.unpaired);

        final DovahLinkTrustState recovered = await relaunched
            .recoverPendingPairing();

        expect(recovered, DovahLinkTrustState.trusted);
        expect(relaunched.trustState, DovahLinkTrustState.trusted);
        final PersistedClientState stored = await storage.load();
        expect(stored.credential, isNotNull);
        expect(stored.recoveryState, PairingRecoveryState.none);
      },
    );

    test(
      'a client whose confirmation the bridge lost after a restart discards it and returns to unpaired',
      () async {
        final HarnessProcess firstHarness = await _startHarness();
        final ClientStorage storage = InMemoryClientStorage();
        final DovahLinkClient client = await _startUnpairedSession(storage);

        final PairingAvailability availability = await client.requestPairing();
        expect(availability, PairingAvailability.available);

        final String code = await _readPairingCode(firstHarness);
        await client.confirmPairingCode(code: code, displayName: 'Bridge Restart Test');
        await client.disconnect();

        // The in-memory pending-credential record never persists past a Bridge restart, per
        // `ai/context/protocol/security.md` -- disposing this harness and starting a fresh one
        // (a genuinely new bridge process, not merely a new connection) recreates that exact loss
        // rather than assuming it.
        await firstHarness.dispose();
        final HarnessProcess secondHarness = await _startHarness();
        addTearDown(secondHarness.dispose);

        final DovahLinkClient relaunched = DovahLinkClient(storage: storage);
        addTearDown(relaunched.disconnect);
        await relaunched.connect(_bridgeUri);
        final HelloResult helloResult = await relaunched.hello();
        expect(helloResult.trustState, DovahLinkTrustState.unpaired);

        final DovahLinkTrustState recovered = await relaunched
            .recoverPendingPairing();

        expect(recovered, DovahLinkTrustState.unpaired);
        // trustState is already `unpaired` from hello() above (a real, fully authenticated
        // session -- just trust-restricted); recovering from pending_not_found leaves it there
        // rather than clearing it, since the session itself was never invalid.
        expect(relaunched.trustState, DovahLinkTrustState.unpaired);
        final PersistedClientState stored = await storage.load();
        expect(stored.credential, isNull);
        expect(stored.recoveryState, PairingRecoveryState.none);
      },
    );
  });
}
