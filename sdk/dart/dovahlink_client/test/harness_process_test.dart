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

/// Runs harness parsing and startup behavior tests.
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

  group('Method parseBridgeInstanceId behaves correctly', () {
    test(
      'Method parseBridgeInstanceId returns the id after a well-formed line',
      () {
        expect(parseBridgeInstanceId('BRIDGE_INSTANCE abc123', ''), 'abc123');
      },
    );

    test(
      'Method parseBridgeInstanceId throws for a line missing the expected prefix',
      () {
        expect(
          () => parseBridgeInstanceId('NOT_AN_INSTANCE_LINE', ''),
          throwsStateError,
        );
      },
    );

    test('Method parseBridgeInstanceId throws for a blank id', () {
      expect(
        () => parseBridgeInstanceId('BRIDGE_INSTANCE ', ''),
        throwsStateError,
      );
    });

    test('Method parseBridgeInstanceId throws for a whitespace-only id', () {
      expect(
        () => parseBridgeInstanceId('BRIDGE_INSTANCE    ', ''),
        throwsStateError,
      );
    });

    test(
      'Method parseBridgeInstanceId embeds stderrOutput in the thrown message',
      () {
        expect(
          () => parseBridgeInstanceId('BRIDGE_INSTANCE ', 'boom'),
          throwsA(
            isA<StateError>().having(
              (error) => error.message,
              'message',
              contains('boom'),
            ),
          ),
        );
      },
    );
  });

  group('Method parsePort behaves correctly', () {
    test('Method parsePort returns the parsed port for a well-formed line', () {
      expect(parsePort('PORT 58231', ''), 58231);
    });

    test('Method parsePort accepts the lowest valid port, 1', () {
      expect(parsePort('PORT 1', ''), 1);
    });

    test('Method parsePort accepts the highest valid port, 65535', () {
      expect(parsePort('PORT 65535', ''), 65535);
    });

    test('Method parsePort throws for a line missing the expected prefix', () {
      expect(() => parsePort('NOT_A_PORT_LINE', ''), throwsStateError);
    });

    test('Method parsePort throws for a non-numeric value', () {
      expect(() => parsePort('PORT not-a-number', ''), throwsStateError);
    });

    test('Method parsePort throws for port 0', () {
      expect(() => parsePort('PORT 0', ''), throwsStateError);
    });

    test('Method parsePort throws for a negative port', () {
      expect(() => parsePort('PORT -1', ''), throwsStateError);
    });

    test('Method parsePort throws for a port above 65535', () {
      expect(() => parsePort('PORT 65536', ''), throwsStateError);
    });

    test('Method parsePort throws for a decimal value', () {
      expect(() => parsePort('PORT 80.5', ''), throwsStateError);
    });

    test('Method parsePort embeds stderrOutput in the thrown message', () {
      expect(
        () => parsePort('PORT 0', 'boom'),
        throwsA(
          isA<StateError>().having(
            (error) => error.message,
            'message',
            contains('boom'),
          ),
        ),
      );
    });
  });
}
