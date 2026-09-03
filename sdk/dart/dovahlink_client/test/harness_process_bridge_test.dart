@Tags(<String>['legacy_bridge'])
library;

import 'dart:io';

import 'package:test/test.dart';

import 'harness_process.dart';

/// A valid 64-character hex-encoded developer token, matching the fixed value the `.NET`
/// integration suite uses (`integration/DovahLinkValidationClient.Tests/BridgeScenario.cs`).
const String _validHexToken =
    '0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344';

/// Builds an isolated trust-store file path so this test never touches the developer's real
/// per-user trust store, mirroring the `.NET` pairing scenarios' own isolation.
String _isolatedTrustStorePath() =>
    '${Directory.systemTemp.path}${Platform.pathSeparator}'
    'dovahlink-trust-${DateTime.now().microsecondsSinceEpoch}.json';

/// Starts an isolated harness, ready for use.
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

/// Legacy Bridge compatibility coverage: proves two `dovahlink_bridge_harness` processes can run
/// concurrently without contending for the same port. Tagged `legacy_bridge` and skipped by
/// default (see `dart_test.yaml`) -- not part of default SDK/App CI, per `ARCHITECTURE.md`'s
/// "Bridge migration and cutover". Run explicitly with `dart test --tags legacy_bridge
/// --run-skipped` after building the harness. `harness_process.dart`'s pure parsing/lookup
/// functions have their own unconditional coverage in `harness_process_test.dart`.
void main() {
  group('Behavior concurrent harness startup behaves correctly', () {
    test('Behavior concurrent harness startup assigns different ports', () async {
      // Proves the actual premise DOVAHLINK_HARNESS_PORT_OVERRIDE exists for: two harnesses
      // alive at once -- not disposed between spawns -- never contend for the same port.
      final HarnessProcess first = await _startHarness();
      final HarnessProcess second = await _startHarness();

      expect(first.bridgeUri.port, isNot(second.bridgeUri.port));
    });
  });
}
