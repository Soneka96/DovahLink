import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_outcome_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Runs [PairingOutcomePayload.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    const Map<PairingOutcome, String> outcomeFixtures =
        <PairingOutcome, String>{
          PairingOutcome.credentialIssued:
              'pairing/pairing-outcome-credential-issued.json',
          PairingOutcome.trusted: 'pairing/pairing-outcome-trusted.json',
          PairingOutcome.alreadyTrusted:
              'pairing/pairing-outcome-already-trusted.json',
          PairingOutcome.expired: 'pairing/pairing-outcome-expired.json',
          PairingOutcome.invalid: 'pairing/pairing-outcome-invalid.json',
          PairingOutcome.pacingLimited:
              'pairing/pairing-outcome-pacing-limited.json',
          PairingOutcome.hardLimitReached:
              'pairing/pairing-outcome-hard-limit-reached.json',
          PairingOutcome.pendingNotFound:
              'pairing/pairing-outcome-pending-not-found.json',
          PairingOutcome.renotified: 'pairing/pairing-outcome-renotified.json',
          PairingOutcome.renotifyCooldown:
              'pairing/pairing-outcome-renotify-cooldown.json',
          PairingOutcome.cancelled: 'pairing/pairing-outcome-cancelled.json',
          PairingOutcome.alreadyIdle:
              'pairing/pairing-outcome-already-idle.json',
          PairingOutcome.pairingInvalidated:
              'pairing/pairing-outcome-invalidated.json',
        };
    // Every one of PairingOutcome's 13 wire values decodes correctly, per
    // `ai/context/sdk/api-design.md`'s "Protocol DTO decoding" full-enum-coverage expectation.
    for (final MapEntry<PairingOutcome, String> entry
        in outcomeFixtures.entries) {
      test('Method fromJson decodes the canonical ${entry.key} fixture', () {
        final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
          _readPayload(entry.value),
        );

        expect(payload.outcome, entry.key);
      });
    }

    test('Method fromJson decodes credential_issued with no shortId yet', () {
      final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
        _readPayload('pairing/pairing-outcome-credential-issued.json'),
      );

      expect(payload.credential, 'a1b2c3d4e5f6');
      expect(payload.shortId, isNull);
      expect(payload.displayName, 'My PC');
      expect(payload.retryAfterSeconds, isNull);
    });

    test('Method fromJson decodes trusted with a credential and shortId', () {
      final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
        _readPayload('pairing/pairing-outcome-trusted.json'),
      );

      expect(payload.credential, 'a1b2c3d4e5f6');
      expect(payload.shortId, '12345');
      expect(payload.displayName, 'My PC');
      expect(payload.retryAfterSeconds, isNull);
    });

    test(
      'Method fromJson decodes already_trusted with a credential and shortId',
      () {
        final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
          _readPayload('pairing/pairing-outcome-already-trusted.json'),
        );

        expect(payload.credential, 'a1b2c3d4e5f6');
        expect(payload.shortId, '12345');
        expect(payload.displayName, 'My PC');
        expect(payload.retryAfterSeconds, isNull);
      },
    );

    test(
      'Method fromJson decodes a failure outcome with no credential, shortId, or displayName',
      () {
        final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
          _readPayload('pairing/pairing-outcome-expired.json'),
        );

        expect(payload.credential, isNull);
        expect(payload.shortId, isNull);
        expect(payload.displayName, isNull);
        expect(payload.retryAfterSeconds, isNull);
      },
    );

    test(
      'Method fromJson decodes retryAfterSeconds for a pacing_limited fixture',
      () {
        final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
          _readPayload('pairing/pairing-outcome-pacing-limited.json'),
        );

        expect(payload.retryAfterSeconds, 1);
      },
    );

    test('Method fromJson decodes pairing_invalidated with no trust data', () {
      final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
        _readPayload('pairing/pairing-outcome-invalidated.json'),
      );

      expect(payload.outcome, PairingOutcome.pairingInvalidated);
      expect(payload.credential, isNull);
      expect(payload.shortId, isNull);
      expect(payload.displayName, isNull);
      expect(payload.retryAfterSeconds, isNull);
    });

    test(
      'Method fromJson accepts zero retryAfterSeconds for pacing_limited',
      () {
        final PairingOutcomePayload payload =
            PairingOutcomePayload.fromJson(<String, dynamic>{
              'outcome': 'pacing_limited',
              'credential': null,
              'shortId': null,
              'displayName': null,
              'retryAfterSeconds': 0,
            });

        expect(payload.retryAfterSeconds, 0);
      },
    );

    test(
      'Method fromJson decodes retryAfterSeconds as null for a hard_limit_reached fixture',
      () {
        final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
          _readPayload('pairing/pairing-outcome-hard-limit-reached.json'),
        );

        expect(payload.retryAfterSeconds, isNull);
      },
    );

    test(
      'Method fromJson decodes retryAfterSeconds for a renotify_cooldown fixture',
      () {
        final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
          _readPayload('pairing/pairing-outcome-renotify-cooldown.json'),
        );

        expect(payload.retryAfterSeconds, 3);
      },
    );

    test(
      'Method fromJson decodes renotified, cancelled, and already_idle with no retryAfterSeconds',
      () {
        for (final String fixture in <String>[
          'pairing/pairing-outcome-renotified.json',
          'pairing/pairing-outcome-cancelled.json',
          'pairing/pairing-outcome-already-idle.json',
        ]) {
          final PairingOutcomePayload payload = PairingOutcomePayload.fromJson(
            _readPayload(fixture),
          );

          expect(payload.retryAfterSeconds, isNull, reason: fixture);
        }
      },
    );

    test(
      'Method fromJson rejects outcome-dependent credential, shortId, displayName, and retryAfterSeconds combinations',
      () {
        const List<JsonMap> invalidPayloads = <JsonMap>[
          <String, dynamic>{
            'outcome': 'credential_issued',
            'credential': null,
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'expired',
            'credential': 'credential-1',
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'credential_issued',
            'credential': '',
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'credential_issued',
            'credential': 'credential-1',
            'shortId': '12345',
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'trusted',
            'credential': 'credential-1',
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'trusted',
            'credential': 'credential-1',
            'shortId': '',
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'trusted',
            'credential': null,
            'shortId': '12345',
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'already_trusted',
            'credential': null,
            'shortId': '12345',
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'already_trusted',
            'credential': 'credential-1',
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'expired',
            'credential': null,
            'shortId': null,
            'displayName': 'My PC',
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'pacing_limited',
            'credential': null,
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'renotify_cooldown',
            'credential': null,
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
          <String, dynamic>{
            'outcome': 'expired',
            'credential': null,
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': 1,
          },
          <String, dynamic>{
            'outcome': 'pairing_invalidated',
            'credential': 'credential-1',
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
        ];

        for (final JsonMap payload in invalidPayloads) {
          expect(
            () => PairingOutcomePayload.fromJson(payload),
            throwsA(isA<ProtocolFormatException>()),
            reason: '$payload is not a valid pairing_outcome payload',
          );
        }
      },
    );

    test(
      'Method fromJson rejects negative and non-integral retryAfterSeconds',
      () {
        for (final Object value in <Object>[-1, 1.5]) {
          expect(
            () => PairingOutcomePayload.fromJson(<String, dynamic>{
              'outcome': 'pacing_limited',
              'credential': null,
              'shortId': null,
              'displayName': null,
              'retryAfterSeconds': value,
            }),
            throwsA(isA<ProtocolFormatException>()),
            reason: '$value is not a valid retryAfterSeconds value',
          );
        }
      },
    );

    test(
      'Method fromJson rejects an unrecognized outcome as ProtocolFormatException',
      () {
        expect(
          () => PairingOutcomePayload.fromJson(<String, dynamic>{
            'outcome': 'not-a-real-outcome',
            'credential': null,
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test('Method fromJson rejects a payload missing a required key', () {
      final JsonMap withMissingKey = _readPayload(
        'pairing/pairing-outcome-trusted.json',
      )..remove('outcome');

      expect(
        () => PairingOutcomePayload.fromJson(withMissingKey),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test(
      'Method fromJson rejects a payload missing each required nullable field',
      () {
        for (final String key in <String>[
          'credential',
          'shortId',
          'displayName',
        ]) {
          final JsonMap withMissingKey = _readPayload(
            'pairing/pairing-outcome-trusted.json',
          )..remove(key);

          expect(
            () => PairingOutcomePayload.fromJson(withMissingKey),
            throwsA(isA<ProtocolFormatException>()),
            reason: '$key is required even when its value may be null',
          );
        }
      },
    );

    test(
      'Method fromJson rejects a payload missing the required retryAfterSeconds key',
      () {
        final JsonMap withMissingKey = _readPayload(
          'pairing/pairing-outcome-trusted.json',
        )..remove('retryAfterSeconds');

        expect(
          () => PairingOutcomePayload.fromJson(withMissingKey),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson rejects a payload with the wrong type for the required outcome key',
      () {
        final JsonMap withWrongType = _readPayload(
          'pairing/pairing-outcome-trusted.json',
        );
        withWrongType['outcome'] = 7;

        expect(
          () => PairingOutcomePayload.fromJson(withWrongType),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson rejects a payload with the wrong type for retryAfterSeconds',
      () {
        final JsonMap withWrongType = _readPayload(
          'pairing/pairing-outcome-pacing-limited.json',
        );
        withWrongType['retryAfterSeconds'] = 'soon';

        expect(
          () => PairingOutcomePayload.fromJson(withWrongType),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson rejects a payload with the wrong type for each nullable string field',
      () {
        for (final String key in <String>[
          'credential',
          'shortId',
          'displayName',
        ]) {
          final JsonMap withWrongType = _readPayload(
            'pairing/pairing-outcome-trusted.json',
          )..[key] = 7;

          expect(
            () => PairingOutcomePayload.fromJson(withWrongType),
            throwsA(isA<ProtocolFormatException>()),
            reason: '$key must be a nullable string',
          );
        }
      },
    );

    test(
      'Method fromJson preserves the contextual validator error boundary',
      () {
        expect(
          () => PairingOutcomePayload.fromJson(<String, dynamic>{
            'outcome': 'expired',
            'credential': 'credential-1',
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          }),
          throwsA(
            isA<ProtocolFormatException>().having(
              (ProtocolFormatException error) => error.message,
              'message',
              startsWith('Invalid pairing_outcome payload:'),
            ),
          ),
        );
      },
    );
  });
}
