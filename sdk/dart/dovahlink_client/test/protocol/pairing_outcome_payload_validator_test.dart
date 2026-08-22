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
              int? retryAfterSeconds,
            })
          >[
            (
              outcome: PairingOutcome.credentialIssued,
              credential: 'credential-1',
              shortId: null,
              displayName: 'My PC',
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.trusted,
              credential: 'credential-1',
              shortId: '12345',
              displayName: 'My PC',
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.alreadyTrusted,
              credential: 'credential-1',
              shortId: '12345',
              displayName: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.expired,
              credential: null,
              shortId: null,
              displayName: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.pacingLimited,
              credential: null,
              shortId: null,
              displayName: null,
              retryAfterSeconds: 1,
            ),
            (
              outcome: PairingOutcome.renotifyCooldown,
              credential: null,
              shortId: null,
              displayName: null,
              retryAfterSeconds: 1,
            ),
          ];

      for (final ({
            PairingOutcome outcome,
            String? credential,
            String? shortId,
            String? displayName,
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
            retryAfterSeconds: validCase.retryAfterSeconds,
            json: <String, dynamic>{
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
              int? retryAfterSeconds,
            })
          >[
            (
              outcome: PairingOutcome.credentialIssued,
              credential: null,
              shortId: null,
              displayName: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.expired,
              credential: 'credential-1',
              shortId: null,
              displayName: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.credentialIssued,
              credential: '',
              shortId: null,
              displayName: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.credentialIssued,
              credential: 'credential-1',
              shortId: '12345',
              displayName: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.trusted,
              credential: 'credential-1',
              shortId: null,
              displayName: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.trusted,
              credential: 'credential-1',
              shortId: '',
              displayName: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.expired,
              credential: null,
              shortId: null,
              displayName: 'My PC',
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.pacingLimited,
              credential: null,
              shortId: null,
              displayName: null,
              retryAfterSeconds: null,
            ),
            (
              outcome: PairingOutcome.expired,
              credential: null,
              shortId: null,
              displayName: null,
              retryAfterSeconds: 1,
            ),
          ];

      for (final ({
            PairingOutcome outcome,
            String? credential,
            String? shortId,
            String? displayName,
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
            retryAfterSeconds: invalidCase.retryAfterSeconds,
            json: <String, dynamic>{
              'retryAfterSeconds': invalidCase.retryAfterSeconds,
            },
          ),
          throwsA(isA<ProtocolFormatException>()),
          reason: '${invalidCase.outcome} has invalid field combinations',
        );
      }
    });

    test('Method validate rejects non-integral and negative raw retry values', () {
      for (final Object value in <Object>[1.5, -1]) {
        expect(
          () => PairingOutcomePayloadValidator.validate(
            outcome: PairingOutcome.pacingLimited,
            credential: null,
            shortId: null,
            displayName: null,
            retryAfterSeconds: null,
            json: <String, dynamic>{'retryAfterSeconds': value},
          ),
          throwsA(isA<ProtocolFormatException>()),
          reason: '$value is not a valid retryAfterSeconds value',
        );
      }
    });

    test('Method validate rejects a non-null raw retry value with an invalid type', () {
      expect(
        () => PairingOutcomePayloadValidator.validate(
          outcome: PairingOutcome.pacingLimited,
          credential: null,
          shortId: null,
          displayName: null,
          retryAfterSeconds: null,
          json: <String, dynamic>{'retryAfterSeconds': 'soon'},
        ),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
