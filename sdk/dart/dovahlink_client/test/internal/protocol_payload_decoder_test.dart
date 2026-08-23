import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs protocol-payload-decoder behavior tests.
void main() {
  group('Method decode behaves correctly', () {
    test('Method decode returns the decoded value on success', () {
      final String result = ProtocolPayloadDecoder.decode(
        (JsonMap json) => json['value'] as String,
        <String, dynamic>{'value': 'ok'},
      );

      expect(result, 'ok');
    });

    test(
      'Method decode translates a ProtocolFormatException into malformed_message',
      () {
        expect(
          () => ProtocolPayloadDecoder.decode(
            (JsonMap json) =>
                throw const ProtocolFormatException('invalid shape'),
            <String, dynamic>{},
          ),
          throwsA(
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException e) => e.code,
                  'code',
                  ProtocolErrorCode.malformedMessage,
                )
                .having(
                  (DovahLinkProtocolException e) => e.message,
                  'message',
                  'invalid shape',
                )
                .having(
                  (DovahLinkProtocolException e) => e.retryable,
                  'retryable',
                  isFalse,
                ),
          ),
        );
      },
    );

    test(
      'Method decode does not catch an exception other than ProtocolFormatException',
      () {
        expect(
          () => ProtocolPayloadDecoder.decode(
            (JsonMap json) => throw StateError('unrelated failure'),
            <String, dynamic>{},
          ),
          throwsA(isA<StateError>()),
        );
      },
    );
  });
}
