import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs [RequestPolicy] behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test(
      'Method constructor preserves retrySafe, requiredTrustState, and timeoutClass',
      () {
        const RequestPolicy policy = RequestPolicy(
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
        const RequestPolicy first = RequestPolicy(
          retrySafe: true,
          requiredTrustState: DovahLinkTrustState.unpaired,
          timeoutClass: TimeoutClass.short,
        );
        // Deliberately non-const: a const literal here would canonicalize to the same instance
        // as `first` regardless of whether `==` is implemented, defeating this test's purpose.
        // ignore: prefer_const_constructors
        final RequestPolicy second = RequestPolicy(
          retrySafe: true,
          requiredTrustState: DovahLinkTrustState.unpaired,
          timeoutClass: TimeoutClass.short,
        );

        expect(first == second, isTrue);
        expect(first.hashCode, second.hashCode);
      },
    );

    test('Operator == returns false when retrySafe differs', () {
      const RequestPolicy first = RequestPolicy(
        retrySafe: true,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.short,
      );
      const RequestPolicy second = RequestPolicy(
        retrySafe: false,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.short,
      );

      expect(first == second, isFalse);
    });

    test('Operator == returns false when requiredTrustState differs', () {
      const RequestPolicy first = RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      );
      const RequestPolicy second = RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.trusted,
        timeoutClass: TimeoutClass.short,
      );

      expect(first == second, isFalse);
    });

    test('Operator == returns false when timeoutClass differs', () {
      const RequestPolicy first = RequestPolicy(
        retrySafe: true,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.short,
      );
      const RequestPolicy second = RequestPolicy(
        retrySafe: true,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.normal,
      );

      expect(first == second, isFalse);
    });

    test(
      'Operator == returns false when compared against a different type',
      () {
        const RequestPolicy policy = RequestPolicy(
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
