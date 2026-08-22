import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

void main() {
  group('ErrorPayload', () {
    group('methods', () {
      test(
        'fromJson decodes a non-retryable authentication failure with no details',
        () {
          final ErrorPayload payload = ErrorPayload.fromJson(
            _readPayload('errors/error-unauthenticated-invalid-token.json'),
          );

          expect(payload.code, ProtocolErrorCode.unauthenticated);
          expect(payload.message, isNotEmpty);
          expect(payload.retryable, isFalse);
          expect(payload.details, isNull);
        },
      );

      test('fromJson decodes a retryable rate-limit failure', () {
        final ErrorPayload payload = ErrorPayload.fromJson(
          _readPayload('errors/error-rate-limited.json'),
        );

        expect(payload.code, ProtocolErrorCode.rateLimited);
        expect(payload.retryable, isTrue);
      });

      test('fromJson decodes a non-retryable revoked-credential failure', () {
        final ErrorPayload payload = ErrorPayload.fromJson(
          _readPayload('errors/error-revoked.json'),
        );

        expect(payload.code, ProtocolErrorCode.revoked);
        expect(payload.retryable, isFalse);
      });

      test('fromJson rejects a payload missing a required key', () {
        final JsonMap withMissingKey = _readPayload(
          'errors/error-rate-limited.json',
        )..remove('code');

        expect(
          () => ErrorPayload.fromJson(withMissingKey),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test('fromJson rejects an unrecognized error code', () {
        final JsonMap withUnknownCode = _readPayload(
          'errors/error-rate-limited.json',
        )..['code'] = 'future_code';

        expect(
          () => ErrorPayload.fromJson(withUnknownCode),
          throwsA(isA<ProtocolFormatException>()),
        );
      });

      test('maps every canonical error code from its exact wire value', () {
        const Map<ProtocolErrorCode, String> wireValues =
            <ProtocolErrorCode, String>{
              ProtocolErrorCode.malformedMessage: 'malformed_message',
              ProtocolErrorCode.frameTooLarge: 'frame_too_large',
              ProtocolErrorCode.unsupportedCapability: 'unsupported_capability',
              ProtocolErrorCode.unauthenticated: 'unauthenticated',
              ProtocolErrorCode.unauthorized: 'unauthorized',
              ProtocolErrorCode.revoked: 'revoked',
              ProtocolErrorCode.blocked: 'blocked',
              ProtocolErrorCode.replayedMessage: 'replayed_message',
              ProtocolErrorCode.staleSession: 'stale_session',
              ProtocolErrorCode.rateLimited: 'rate_limited',
              ProtocolErrorCode.internalError: 'internal_error',
            };

        expect(wireValues.keys, ProtocolErrorCode.values);
        for (final MapEntry<ProtocolErrorCode, String> entry
            in wireValues.entries) {
          final JsonMap json = _readPayload('errors/error-rate-limited.json')
            ..['code'] = entry.value;

          expect(ErrorPayload.fromJson(json).code, entry.key);
        }
      });

      test(
        'fromJson rejects a payload with the wrong type for a required key',
        () {
          final JsonMap withWrongType = _readPayload(
            'errors/error-rate-limited.json',
          );
          withWrongType['retryable'] = 'not a bool';

          expect(
            () => ErrorPayload.fromJson(withWrongType),
            throwsA(isA<ProtocolFormatException>()),
          );
        },
      );

      test(
        'fromJson accepts a payload with the details key entirely absent',
        () {
          final JsonMap withoutDetailsKey = _readPayload(
            'errors/error-rate-limited.json',
          )..remove('details');

          final ErrorPayload payload = ErrorPayload.fromJson(withoutDetailsKey);

          expect(payload.details, isNull);
        },
      );

      test('fromJson decodes a populated details object', () {
        // No canonical fixture carries a populated `details` object yet, so this hand-builds the
        // one case none of the shared error fixtures cover.
        final JsonMap withDetails = _readPayload(
          'errors/error-rate-limited.json',
        );
        withDetails['details'] = <String, dynamic>{'retryAfterMs': 500};

        final ErrorPayload payload = ErrorPayload.fromJson(withDetails);

        expect(payload.details, <String, dynamic>{'retryAfterMs': 500});
      });
    });
  });
}
