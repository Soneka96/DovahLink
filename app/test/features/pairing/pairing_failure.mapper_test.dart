import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/pairing_failure.mapper.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkPairingException, PairingOutcome;

/// Exercises the SDK-to-app pairing-failure boundary.
void main() {
  group('Method fromSdkException behaves correctly', () {
    test('PairingFailureMapper maps every Host failure outcome', () {
      for (final (PairingOutcome sdkOutcome, PairingFailureOutcome appOutcome)
          in const [
            (PairingOutcome.expired, PairingFailureOutcome.expired),
            (PairingOutcome.invalid, PairingFailureOutcome.invalid),
            (PairingOutcome.pacingLimited, PairingFailureOutcome.pacingLimited),
            (
              PairingOutcome.hardLimitReached,
              PairingFailureOutcome.hardLimitReached,
            ),
            (
              PairingOutcome.pendingNotFound,
              PairingFailureOutcome.pendingNotFound,
            ),
            (
              PairingOutcome.pairingInvalidated,
              PairingFailureOutcome.pairingInvalidated,
            ),
          ]) {
        final PairingFailure? failure = PairingFailureMapper.fromSdkException(
          DovahLinkPairingException(sdkOutcome, attemptsRemaining: 2),
          fallbackMessage: 'fallback',
        );

        expect(failure, isA<PairingFailure>());
        expect(failure!.outcome, appOutcome);
        expect(failure.attemptsRemaining, 2);
      }
    });

    test(
      'PairingFailureMapper keeps only invalid and pacing outcomes retriable',
      () {
        final PairingFailure? invalid = PairingFailureMapper.fromSdkException(
          const DovahLinkPairingException(
            PairingOutcome.invalid,
            attemptsRemaining: 2,
          ),
          fallbackMessage: 'fallback',
          keepCodeEntry: true,
        );
        final PairingFailure? paced = PairingFailureMapper.fromSdkException(
          const DovahLinkPairingException(PairingOutcome.pacingLimited),
          fallbackMessage: 'fallback',
          keepCodeEntry: true,
        );
        final PairingFailure? expired = PairingFailureMapper.fromSdkException(
          const DovahLinkPairingException(PairingOutcome.expired),
          fallbackMessage: 'fallback',
          keepCodeEntry: true,
        );

        expect(invalid, isA<PairingRetriableFailure>());
        expect(paced, isA<PairingRetriableFailure>());
        expect(expired, isA<PairingFailure>());
        expect(expired, isNot(isA<PairingRetriableFailure>()));
      },
    );

    test('PairingFailureMapper rejects outcomes from other exchanges', () {
      expect(
        PairingFailureMapper.fromSdkException(
          const DovahLinkPairingException(PairingOutcome.credentialIssued),
          fallbackMessage: 'fallback',
        ),
        isNull,
      );
    });
  });
}
