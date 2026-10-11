import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/compatibility/host_version_compatibility.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Finds the repository's root [VERSION] file by searching from [startingDirectory].
/// @param startingDirectory The directory from which to search for the repository root.
/// @return The repository's root version file.
/// @throws TestFailure If no repository root is found before reaching the filesystem root.
File _findRepositoryVersionFile(Directory startingDirectory) {
  Directory directory = startingDirectory;
  while (true) {
    final File versionFile = File(
      '${directory.path}${Platform.pathSeparator}VERSION',
    );
    if (versionFile.existsSync()) {
      return versionFile;
    }

    final Directory parent = directory.parent;
    if (parent.path == directory.path) {
      fail(
        'The repository VERSION file was not found from ${startingDirectory.path}.',
      );
    }
    directory = parent;
  }
}

/// Expects [hostVersion] to be rejected as [failure] against the declared range.
void _expectIncompatible(
  String hostVersion,
  HostVersionCompatibilityFailure failure,
) {
  expect(
    () => validateHostVersionCompatibility(hostVersion),
    throwsA(
      isA<DovahLinkCompatibilityException>()
          .having(
            (DovahLinkCompatibilityException error) => error.hostVersion,
            'hostVersion',
            hostVersion,
          )
          .having(
            (DovahLinkCompatibilityException error) => error.failure,
            'failure',
            failure,
          )
          .having(
            (DovahLinkCompatibilityException error) =>
                error.supportedHostVersionRange,
            'supportedHostVersionRange',
            '0.6.x',
          ),
    ),
    reason: '$hostVersion is outside the declared range',
  );
}

/// Runs Host-version compatibility behavior tests.
void main() {
  group('Method validateHostVersionCompatibility behaves correctly', () {
    test(
      'Method validateHostVersionCompatibility accepts the declared minor and ignores patch versions',
      () {
        for (final String hostVersion in <String>[
          '0.6.0',
          '0.6.1',
          '0.6.999',
        ]) {
          expect(
            () => validateHostVersionCompatibility(hostVersion),
            returnsNormally,
            reason: '$hostVersion is inside the declared range',
          );
        }
      },
    );

    test(
      'Method validateHostVersionCompatibility rejects every earlier Host, including 0.5.x, as too old',
      () {
        for (final String hostVersion in <String>[
          '0.5.0',
          '0.5.9',
          '0.4.0',
          '0.0.1',
        ]) {
          _expectIncompatible(
            hostVersion,
            HostVersionCompatibilityFailure.hostTooOld,
          );
        }
      },
    );

    test(
      'Method validateHostVersionCompatibility classifies a newer Host version',
      () {
        for (final String hostVersion in <String>[
          '0.7.0',
          '0.7.9',
          '1.0.0',
          '2.3.4',
        ]) {
          _expectIncompatible(
            hostVersion,
            HostVersionCompatibilityFailure.hostTooNew,
          );
        }
      },
    );

    test(
      'Method validateHostVersionCompatibility rejects a malformed Host version as a protocol error',
      () {
        for (final String hostVersion in <String>[
          '',
          '0.6',
          '00.6.0',
          '0.6.0-beta',
          'v0.6.0',
          ' 0.6.0',
          '0.6.0 ',
        ]) {
          expect(
            () => validateHostVersionCompatibility(hostVersion),
            throwsA(
              isA<DovahLinkProtocolException>().having(
                (DovahLinkProtocolException error) => error.code,
                'code',
                ProtocolErrorCode.malformedMessage,
              ),
            ),
            reason: '$hostVersion is not a release version',
          );
        }
      },
    );

    test(
      'Property supportedHostVersionRange names the accepted minor line',
      () {
        expect(supportedHostVersionRange, '0.6.x');
      },
    );

    test(
      'Method validateHostVersionCompatibility accepts the repository release version from the package and repository root',
      () {
        final File packageVersionFile = _findRepositoryVersionFile(
          Directory.current,
        );
        final Directory repositoryRoot = packageVersionFile.parent;
        final File repositoryVersionFile = _findRepositoryVersionFile(
          repositoryRoot,
        );
        expect(repositoryVersionFile.path, packageVersionFile.path);
        final String repositoryVersion = repositoryVersionFile
            .readAsStringSync()
            .trim();

        expect(
          () => validateHostVersionCompatibility(repositoryVersion),
          returnsNormally,
          reason: 'root VERSION must sit inside the SDK-declared Host range',
        );
      },
    );
  });
}
