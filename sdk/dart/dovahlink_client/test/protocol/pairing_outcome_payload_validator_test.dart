import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/pairing_outcome_payload_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs pairing-outcome validator behavior tests.
void main() {
  group('Method validate behaves correctly', () {
    test('Method validate accepts each allowed outcome field shape', () {
      const List<
        ({
          PairingOutcome outcome,
          String? credential,
          String? shortId,
          String? displayName,
          int? attemptsRemaining,
          int? retryAfterSeconds,
        })
      >
      validCases =
          <
            ({
              PairingOutcome outcome,
              String? credential,
              String? shortId,
              String? displayName,
              int? attemptsRemaining,
              int? retryAfterSeconds,
            })
          >[
            (
              outcome: PairingOutcome.credentialIssued,
              credential: 'credential-1',
              shortId: null,
              displayName: 'My PC',
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.trusted,
              credential: 'credential-1',
              shortId: '12345',
              displayName: 'My PC',
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.alreadyTrusted,
              credential: 'credential-1',
              shortId: '12345',
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.expired,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.pacingLimited,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: 1,
            ),
            (
              outcome: PairingOutcome.renotifyCooldown,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: 1,
            ),
            (
              outcome: PairingOutcome.renotified,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: 5,
            ),
            (
              outcome: PairingOutcome.invalid,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.invalid,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: 4,
              retryAfterSeconds: null,
            ),
          ];

      for (final ({
            PairingOutcome outcome,
            String? credential,
            String? shortId,
            String? displayName,
            int? attemptsRemaining,
            int? retryAfterSeconds,
          })
          validCase
          in validCases) {
        expect(
          () => PairingOutcomePayloadValidator.validate(
            outcome: validCase.outcome,
            credential: validCase.credential,
            shortId: validCase.shortId,
            displayName: validCase.displayName,
            attemptsRemaining: validCase.attemptsRemaining,
            retryAfterSeconds: validCase.retryAfterSeconds,
            json: <String, dynamic>{
              'attemptsRemaining': validCase.attemptsRemaining,
              'retryAfterSeconds': validCase.retryAfterSeconds,
            },
          ),
          returnsNormally,
          reason: '${validCase.outcome} is a valid pairing outcome',
        );
      }
    });

    test('Method validate rejects outcome-dependent field combinations', () {
      const List<
        ({
          PairingOutcome outcome,
          String? credential,
          String? shortId,
          String? displayName,
          int? attemptsRemaining,
          int? retryAfterSeconds,
        })
      >
      invalidCases =
          <
            ({
              PairingOutcome outcome,
              String? credential,
              String? shortId,
              String? displayName,
              int? attemptsRemaining,
              int? retryAfterSeconds,
            })
          >[
            (
              outcome: PairingOutcome.credentialIssued,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.expired,
              credential: 'credential-1',
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.credentialIssued,
              credential: '',
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.credentialIssued,
              credential: 'credential-1',
              shortId: '12345',
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.trusted,
              credential: 'credential-1',
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.trusted,
              credential: 'credential-1',
              shortId: '',
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.expired,
              credential: null,
              shortId: null,
              displayName: 'My PC',
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.pacingLimited,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.expired,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: 1,
            ),
            (
              outcome: PairingOutcome.expired,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: 4,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.invalid,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: 0,
              retryAfterSeconds: null,
            ),
          ];

      for (final ({
            PairingOutcome outcome,
            String? credential,
            String? shortId,
            String? displayName,
            int? attemptsRemaining,
            int? retryAfterSeconds,
          })
          invalidCase
          in invalidCases) {
        expect(
          () => PairingOutcomePayloadValidator.validate(
            outcome: invalidCase.outcome,
            credential: invalidCase.credential,
            shortId: invalidCase.shortId,
            displayName: invalidCase.displayName,
            attemptsRemaining: invalidCase.attemptsRemaining,
            retryAfterSeconds: invalidCase.retryAfterSeconds,
            json: <String, dynamic>{
              'attemptsRemaining': invalidCase.attemptsRemaining,
              'retryAfterSeconds': invalidCase.retryAfterSeconds,
            },
          ),
          throwsA(isA<ProtocolFormatException>()),
          reason: '${invalidCase.outcome} has invalid field combinations',
        );
      }
    });

    test(
      'Method validate rejects non-integral and negative raw retry values',
      () {
        for (final Object value in <Object>[1.5, -1]) {
          expect(
            () => PairingOutcomePayloadValidator.validate(
              outcome: PairingOutcome.pacingLimited,
              credential: null,
              shortId: null,
              displayName: null,
              attemptsRemaining: null,
              retryAfterSeconds: null,
              json: <String, dynamic>{
                'attemptsRemaining': null,
                'retryAfterSeconds': value,
              },
            ),
            throwsA(isA<ProtocolFormatException>()),
            reason: '$value is not a valid retryAfterSeconds value',
          );
        }
      },
    );

    test('Method validate rejects invalid raw attemptsRemaining values', () {
      for (final Object value in <Object>[-1, 1.5, 'four']) {
        expect(
          () => PairingOutcomePayloadValidator.validate(
            outcome: PairingOutcome.invalid,
            credential: null,
            shortId: null,
            displayName: null,
            attemptsRemaining: null,
            retryAfterSeconds: null,
            json: <String, dynamic>{
              'attemptsRemaining': value,
              'retryAfterSeconds': null,
            },
          ),
          throwsA(isA<ProtocolFormatException>()),
          reason: '$value is not a valid attemptsRemaining value',
        );
      }
    });

    test(
      'Method validate rejects a non-null raw retry value with an invalid type',
      () {
        expect(
          () => PairingOutcomePayloadValidator.validate(
            outcome: PairingOutcome.pacingLimited,
            credential: null,
            shortId: null,
            displayName: null,
            attemptsRemaining: null,
            retryAfterSeconds: null,
            json: <String, dynamic>{
              'attemptsRemaining': null,
              'retryAfterSeconds': 'soon',
            },
          ),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );
  });
}
