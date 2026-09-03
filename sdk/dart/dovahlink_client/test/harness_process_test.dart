import 'dart:io';

import 'package:test/test.dart';

import 'harness_process.dart';

/// Runs harness parsing and lookup behavior tests. This coverage is unconditional -- no process
/// spawning, no Bridge harness required -- unlike `harness_process_bridge_test.dart`'s
/// `legacy_bridge`-tagged concurrent-startup coverage of `HarnessProcess.start`/`dispose`
/// themselves.
void main() {
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

  group('Method locateHarnessExecutable behaves correctly', () {
    test(
      'Method locateHarnessExecutable finds the executable under an ancestor',
      () {
        final Directory root = Directory.systemTemp.createTempSync(
          'dovahlink-harness-location-',
        );
        addTearDown(() => root.deleteSync(recursive: true));
        final Directory nested = Directory('${root.path}/nested')..createSync();
        final File executable = File(
          '${root.path}/bridge/build/windows-x64-debug/'
          'dovahlink_bridge_harness.exe',
        )..createSync(recursive: true);

        expect(
          Uri.file(locateHarnessExecutable(startingDirectory: nested)),
          Uri.file(executable.path),
        );
      },
    );

    test(
      'Method locateHarnessExecutable throws when no executable exists in the search path',
      () {
        final Directory root = Directory.systemTemp.createTempSync(
          'dovahlink-harness-location-',
        );
        addTearDown(() => root.deleteSync(recursive: true));

        expect(
          () => locateHarnessExecutable(startingDirectory: root),
          throwsStateError,
        );
      },
    );
  });
}
