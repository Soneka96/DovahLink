import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
import '../../../../../fixtures/fixtures.dart';

/// Exercises [AuthenticateParams] value equality.
void main() {
  group('Behavior equality behaves correctly', () {
    test('AuthenticateParams equal when their hostUri is equal', () {
      final AuthenticateParams first = Fixtures.buildAuthenticateParams();
      final AuthenticateParams second = Fixtures.buildAuthenticateParams();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('AuthenticateParams differ when their hostUri differs', () {
      final AuthenticateParams first = Fixtures.buildAuthenticateParams();
      final AuthenticateParams second = Fixtures.buildAuthenticateParams(
        hostUri: Uri.parse('ws://192.168.1.20:4000/'),
      );

      expect(first, isNot(second));
    });
  });
}
