import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/reply_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/protocol/envelope.fixture.dart';

/// Runs reply-validator behavior tests.
void main() {
  group('Method validate behaves correctly', () {
    test('Method validate returns a reply with the expected message type', () {
      final Envelope reply = buildEnvelope(
        messageType: ProtocolMessageType.pong,
      );

      expect(
        ReplyValidator.validate(
          expectedType: ProtocolMessageType.pong,
          envelope: reply,
        ),
        same(reply),
      );
    });

    test(
      'Method validate converts a valid wire error into a typed exception',
      () {
        final Envelope reply = buildEnvelope(
          messageType: ProtocolMessageType.error,
          payload: const <String, dynamic>{
            'code': 'rate_limited',
            'message': 'try again',
            'retryable': true,
          },
        );

        expect(
          () => ReplyValidator.validate(
            expectedType: ProtocolMessageType.pong,
            envelope: reply,
          ),
          throwsA(
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException error) => error.code,
                  'code',
                  ProtocolErrorCode.rateLimited,
                )
                .having(
                  (DovahLinkProtocolException error) => error.retryable,
                  'retryable',
                  isTrue,
                )
                .having(
                  (DovahLinkProtocolException error) => error.message,
                  'message',
                  'try again',
                ),
          ),
        );
      },
    );

    test(
      'Method validate translates a malformed wire error into malformed_message',
      () {
        final Envelope reply = buildEnvelope(
          messageType: ProtocolMessageType.error,
          payload: const <String, dynamic>{
            'code': 'future_error_code',
            'message': 'malformed code',
            'retryable': false,
          },
        );

        expect(
          () => ReplyValidator.validate(
            expectedType: ProtocolMessageType.pong,
            envelope: reply,
          ),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
      },
    );

    test('Method validate rejects an unexpected message type', () {
      final Envelope reply = buildEnvelope(
        messageType: ProtocolMessageType.ping,
      );

      expect(
        () => ReplyValidator.validate(
          expectedType: ProtocolMessageType.pong,
          envelope: reply,
        ),
        throwsA(
          isA<DovahLinkProtocolException>().having(
            (DovahLinkProtocolException error) => error.code,
            'code',
            ProtocolErrorCode.malformedMessage,
          ),
        ),
      );
    });
  });
}
