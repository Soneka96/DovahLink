import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'fixtures/fixtures.dart';

/// Runs [RequestPolicy] behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test(
      'Method constructor preserves retrySafe, requiredTrustState, and timeoutClass',
      () {
        final RequestPolicy policy = Fixtures.buildRequestPolicy(
          retrySafe: true,
          requiredTrustState: DovahLinkTrustState.unpaired,
          timeoutClass: TimeoutClass.short,
        );

        expect(policy.retrySafe, isTrue);
        expect(policy.requiredTrustState, DovahLinkTrustState.unpaired);
        expect(policy.timeoutClass, TimeoutClass.short);
      },
    );
  });

  group('Operator == behaves correctly', () {
    test(
      'Operator == returns true for two separately-constructed instances with identical fields',
      () {
        final RequestPolicy first = Fixtures.buildRequestPolicy(
          retrySafe: true,
          requiredTrustState: DovahLinkTrustState.unpaired,
          timeoutClass: TimeoutClass.short,
        );
        final RequestPolicy second = Fixtures.buildRequestPolicy(
          retrySafe: true,
          requiredTrustState: DovahLinkTrustState.unpaired,
          timeoutClass: TimeoutClass.short,
        );

        expect(first == second, isTrue);
        expect(first.hashCode, second.hashCode);
      },
    );

    test('Operator == returns false when retrySafe differs', () {
      final RequestPolicy first = Fixtures.buildRequestPolicy(
        retrySafe: true,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.short,
      );
      final RequestPolicy second = Fixtures.buildRequestPolicy(
        retrySafe: false,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.short,
      );

      expect(first == second, isFalse);
    });

    test('Operator == returns false when requiredTrustState differs', () {
      final RequestPolicy first = Fixtures.buildRequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      );
      final RequestPolicy second = Fixtures.buildRequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.trusted,
        timeoutClass: TimeoutClass.short,
      );

      expect(first == second, isFalse);
    });

    test('Operator == returns false when timeoutClass differs', () {
      final RequestPolicy first = Fixtures.buildRequestPolicy(
        retrySafe: true,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.short,
      );
      final RequestPolicy second = Fixtures.buildRequestPolicy(
        retrySafe: true,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.normal,
      );

      expect(first == second, isFalse);
    });

    test(
      'Operator == returns false when compared against a different type',
      () {
        final RequestPolicy policy = Fixtures.buildRequestPolicy(
          retrySafe: true,
          requiredTrustState: null,
          timeoutClass: TimeoutClass.short,
        );

        // ignore: unrelated_type_equality_checks
        expect(policy == 'not a policy', isFalse);
      },
    );
  });
}
