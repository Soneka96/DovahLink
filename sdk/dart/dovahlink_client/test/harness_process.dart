import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:async/async.dart';

/// Launches and controls one deterministic bridge harness subprocess
/// (`bridge/harness/dovahlink_bridge_harness.cpp`) for real end-to-end tests. Mirrors
/// `integration/DovahLinkValidationClient.Tests/HarnessProcess.cs`'s mechanics for a Dart caller.
///
/// Every instance binds the bridge's fixed, documented loopback port -- the `.NET` integration
/// suite solves the same constraint by disabling test-class parallelism outright
/// (`integration/README.md`). `package:test` can run separate test files concurrently by default;
/// repeated runs of this project's harness-using files together, including with concurrency
/// forced above the default, have not shown a collision, so no such restriction is configured
/// here yet. Add one (e.g. a shared `dart_test.yaml` tag with `concurrency: 1`) if that changes.
class HarnessProcess {
  HarnessProcess._(this._process, this._stdoutLines, this._stderrBuffer);

  /// The default time allowed for one harness output line.
  static const Duration _defaultReadTimeout = Duration(seconds: 10);

  /// The running harness process.
  final Process _process;

  /// Pulls one decoded stdout line at a time from the harness.
  final StreamQueue<String> _stdoutLines;

  /// Accumulates decoded standard-error output for diagnostics. Draining this stream (rather than
  /// leaving it unread) also avoids the OS pipe buffer filling and blocking the harness's own
  /// stderr writes.
  final StringBuffer _stderrBuffer;

  /// Standard-error output captured from the harness so far.
  String get stderrOutput => _stderrBuffer.toString();

  /// Starts the harness with [token] as its bridge token and any [extraEnvironment] variables.
  ///
  /// Register `addTearDown(harness.dispose)` immediately after this returns, before calling
  /// [waitForReady] or anything else that can throw -- nothing here disposes the process
  /// automatically on a later failure.
  static Future<HarnessProcess> start({
    required String token,
    Map<String, String> extraEnvironment = const <String, String>{},
  }) async {
    final Process process = await Process.start(
      _locateHarnessExecutable(),
      <String>[],
      environment: <String, String>{
        'DOVAHLINK_BRIDGE_TOKEN': token,
        ...extraEnvironment,
      },
    );
    final StreamQueue<String> stdoutLines = StreamQueue<String>(
      process.stdout.transform(utf8.decoder).transform(const LineSplitter()),
    );
    final StringBuffer stderrBuffer = StringBuffer();
    process.stderr.transform(utf8.decoder).listen(stderrBuffer.write);
    return HarnessProcess._(process, stdoutLines, stderrBuffer);
  }

  /// Reads and validates the harness startup handshake: `READY` followed by `BRIDGE_INSTANCE <id>`.
  /// @return The reported bridge instance ID.
  Future<String> waitForReady({Duration timeout = _defaultReadTimeout}) async {
    final String ready = await readLine(timeout: timeout);
    if (ready != 'READY') {
      throw StateError(
        'Harness did not report READY: $ready. Stderr: $stderrOutput',
      );
    }

    const String prefix = 'BRIDGE_INSTANCE ';
    final String instanceLine = await readLine(timeout: timeout);
    if (!instanceLine.startsWith(prefix)) {
      throw StateError(
        'Harness did not report its bridge instance ID: $instanceLine. Stderr: $stderrOutput',
      );
    }
    return instanceLine.substring(prefix.length);
  }

  /// Reads the next harness stdout line within [timeout].
  ///
  /// A [TimeoutException] only abandons this wait -- it does not cancel the underlying read, so a
  /// line that arrives just after the timeout is still delivered to whichever [readLine] call
  /// comes next. Treat a timeout as fatal to this instance (call [dispose] and fail the test)
  /// rather than continuing to read from it.
  Future<String> readLine({Duration timeout = _defaultReadTimeout}) async {
    final bool hasNext = await _stdoutLines.hasNext.timeout(
      timeout,
      onTimeout: () => throw TimeoutException(
        'Harness did not write a line within $timeout',
      ),
    );
    if (!hasNext) {
      throw StateError('Harness output ended before producing a line.');
    }
    return _stdoutLines.next;
  }

  /// Writes one line to the harness's stdin.
  void writeLine(String line) {
    _process.stdin.writeln(line);
  }

  /// Terminates the harness process and releases its resources.
  Future<void> dispose() async {
    _process.kill(ProcessSignal.sigkill);
    await _process.exitCode;
  }

  /// Locates the Windows debug-build DovahLink bridge harness executable by walking up from the
  /// current working directory, mirroring `HarnessProcess.cs`'s own search.
  static String _locateHarnessExecutable() {
    Directory? directory = Directory.current;
    for (int i = 0; i < 10 && directory != null; i++) {
      final File candidate = File(
        '${directory.path}${Platform.pathSeparator}bridge${Platform.pathSeparator}build'
        '${Platform.pathSeparator}windows-x64-debug${Platform.pathSeparator}dovahlink_bridge_harness.exe',
      );
      if (candidate.existsSync()) {
        return candidate.path;
      }
      final Directory parent = directory.parent;
      directory = parent.path == directory.path ? null : parent;
    }
    throw StateError(
      'Could not find dovahlink_bridge_harness.exe under any ancestor of ${Directory.current.path}. '
      'Build it first: cmake --build --preset windows-x64-debug --target dovahlink_bridge_harness',
    );
  }
}
