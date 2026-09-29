import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
import '../../../../../fixtures/fixtures.dart';

/// Exercises [AuthenticateParams] value equality.
void main() {
  group('Behavior equality behaves correctly', () {
    test('AuthenticateParams equal when their candidate endpoint is equal', () {
      final AuthenticateParams first = Fixtures.buildAuthenticateParams();
      final AuthenticateParams second = Fixtures.buildAuthenticateParams();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('AuthenticateParams differ when their candidate endpoint differs', () {
      final AuthenticateParams first = Fixtures.buildAuthenticateParams();
      final AuthenticateParams second = Fixtures.buildAuthenticateParams(
        hostUri: Uri.parse('ws://192.168.1.20:4000/'),
      );

      expect(first, isNot(second));
    });

    test(
      'AuthenticateParams equality distinguishes a Known Host ID target',
      () {
        final AuthenticateParams first = Fixtures.buildAuthenticateParams(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        final AuthenticateParams same = Fixtures.buildAuthenticateParams(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        final AuthenticateParams another = Fixtures.buildAuthenticateParams(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
        );

        expect(first, same);
        expect(first.hashCode, same.hashCode);
        expect(first, isNot(another));
      },
    );
  });
}
