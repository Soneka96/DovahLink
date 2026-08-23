import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_rejection_classifier.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The four codes [ReconnectRejectionClassifier.isTerminal] must classify terminal regardless of
/// [DovahLinkProtocolException.retryable].
const List<ProtocolErrorCode> _alwaysTerminalCodes = <ProtocolErrorCode>[
  ProtocolErrorCode.revoked,
  ProtocolErrorCode.blocked,
  ProtocolErrorCode.unauthorized,
  ProtocolErrorCode.malformedMessage,
];

/// Every other code, whose classification must follow [DovahLinkProtocolException.retryable]
/// alone -- exhaustive so a code accidentally added to (or missing from) the always-terminal set
/// surfaces as a failing test here, not just for the two codes a hand-picked sample would cover.
final List<ProtocolErrorCode> _flagDrivenCodes = ProtocolErrorCode.values
    .where((ProtocolErrorCode code) => !_alwaysTerminalCodes.contains(code))
    .toList();

/// Runs reconnect-rejection-classifier behavior tests.
void main() {
  group('Method isTerminal behaves correctly', () {
    test(
      'Method isTerminal returns true for each always-terminal administrative code even when '
      'retryable is true',
      () {
        for (final ProtocolErrorCode code in _alwaysTerminalCodes) {
          final DovahLinkProtocolException error = DovahLinkProtocolException(
            code: code,
            message: 'test',
            retryable: true,
          );

          expect(
            ReconnectRejectionClassifier.isTerminal(error),
            isTrue,
            reason: '$code should be terminal even when retryable is true',
          );
        }
      },
    );

    test(
      'Method isTerminal returns true for every other code the bridge reported as not retryable',
      () {
        for (final ProtocolErrorCode code in _flagDrivenCodes) {
          final DovahLinkProtocolException error = DovahLinkProtocolException(
            code: code,
            message: 'test',
            retryable: false,
          );

          expect(
            ReconnectRejectionClassifier.isTerminal(error),
            isTrue,
            reason: '$code should be terminal when retryable is false',
          );
        }
      },
    );

    test(
      'Method isTerminal returns false for every other code the bridge reported as retryable',
      () {
        for (final ProtocolErrorCode code in _flagDrivenCodes) {
          final DovahLinkProtocolException error = DovahLinkProtocolException(
            code: code,
            message: 'test',
            retryable: true,
          );

          expect(
            ReconnectRejectionClassifier.isTerminal(error),
            isFalse,
            reason: '$code should be retryable when retryable is true',
          );
        }
      },
    );
  });
}
