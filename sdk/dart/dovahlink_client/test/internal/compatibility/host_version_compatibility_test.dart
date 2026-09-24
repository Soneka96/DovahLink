import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/compatibility/host_version_compatibility.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs Host-version compatibility behavior tests.
void main() {
  group('Method validateHostVersionCompatibility behaves correctly', () {
    test(
      'Method validateHostVersionCompatibility accepts the declared minor and ignores patch versions',
      () {
        expect(
          () => validateHostVersionCompatibility('0.5.0'),
          returnsNormally,
        );
        expect(
          () => validateHostVersionCompatibility('0.5.1'),
          returnsNormally,
        );
        expect(
          () => validateHostVersionCompatibility('0.5.999'),
          returnsNormally,
        );
      },
    );

    test(
      'Method validateHostVersionCompatibility classifies an older Host version',
      () {
        expect(
          () => validateHostVersionCompatibility('0.4.0'),
          throwsA(
            isA<DovahLinkCompatibilityException>()
                .having(
                  (DovahLinkCompatibilityException error) => error.hostVersion,
                  'hostVersion',
                  '0.4.0',
                )
                .having(
                  (DovahLinkCompatibilityException error) => error.failure,
                  'failure',
                  HostVersionCompatibilityFailure.hostTooOld,
                )
                .having(
                  (DovahLinkCompatibilityException error) =>
                      error.supportedHostVersionRange,
                  'supportedHostVersionRange',
                  '0.5.x',
                ),
          ),
        );
      },
    );

    test(
      'Method validateHostVersionCompatibility classifies a newer Host version',
      () {
        expect(
          () => validateHostVersionCompatibility('0.6.0'),
          throwsA(
            isA<DovahLinkCompatibilityException>().having(
              (DovahLinkCompatibilityException error) => error.failure,
              'failure',
              HostVersionCompatibilityFailure.hostTooNew,
            ),
          ),
        );
      },
    );

    test(
      'Method validateHostVersionCompatibility rejects a malformed Host version as a protocol error',
      () {
        for (final String hostVersion in <String>[
          '',
          '0.5',
          '00.5.0',
          '0.5.0-beta',
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
  });
}
