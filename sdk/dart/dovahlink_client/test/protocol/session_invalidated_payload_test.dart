import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/session_invalidated_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs session-invalidation decoding behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    const Map<AdministrativeInvalidationReason, String> wireReasons =
        <AdministrativeInvalidationReason, String>{
          AdministrativeInvalidationReason.revoked: 'revoked',
          AdministrativeInvalidationReason.blocked: 'blocked',
          AdministrativeInvalidationReason.trustReset: 'trust_reset',
          AdministrativeInvalidationReason.factoryReset: 'factory_reset',
        };
    for (final MapEntry<AdministrativeInvalidationReason, String> entry
        in wireReasons.entries) {
      test('Method fromJson decodes the wire value "${entry.value}"', () {
        final SessionInvalidatedPayload payload =
            SessionInvalidatedPayload.fromJson(<String, dynamic>{
              'reason': entry.value,
            });

        expect(payload.reason, entry.key);
      });
    }

    test(
      'Method fromJson rejects an unrecognized reason as ProtocolFormatException',
      () {
        expect(
          () => SessionInvalidatedPayload.fromJson(<String, dynamic>{
            'reason': 'not-a-real-reason',
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson rejects a payload missing the required reason key',
      () {
        expect(
          () => SessionInvalidatedPayload.fromJson(<String, dynamic>{}),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson rejects a payload with the wrong type for the required reason key',
      () {
        expect(
          () => SessionInvalidatedPayload.fromJson(<String, dynamic>{
            'reason': 7,
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson rejects a payload with a null value for the required reason key',
      () {
        expect(
          () => SessionInvalidatedPayload.fromJson(<String, dynamic>{
            'reason': null,
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );
  });
}
