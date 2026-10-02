import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_renotify_result.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises the app-owned typed code-redisplay result.
void main() {
  group('Behavior equality behaves correctly', () {
    test('PairingRenotifyResult compares outcome and retry interval', () {
      final PairingRenotifyResult first = Fixtures.buildPairingRenotifyResult();
      final PairingRenotifyResult equal = Fixtures.buildPairingRenotifyResult();
      final PairingRenotifyResult cooled = Fixtures.buildPairingRenotifyResult(
        outcome: PairingRenotifyOutcome.cooldown,
      );
      final PairingRenotifyResult shorter = Fixtures.buildPairingRenotifyResult(
        retryAfterSeconds: 3,
      );

      expect(first, equal);
      expect(first.hashCode, equal.hashCode);
      expect(first, isNot(cooled));
      expect(first, isNot(shorter));
    });
  });
}
