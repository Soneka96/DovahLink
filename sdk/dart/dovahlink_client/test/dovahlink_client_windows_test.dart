import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart' show IClientStorage;
import 'package:dovahlink_client_sdk/dovahlink_client_windows.dart'
    show DpapiClientStorage;

/// Runs Windows SDK entry-point tests.
void main() {
  group('Windows SDK entry point exposes platform storage', () {
    test(
      'Windows SDK entry point exposes DpapiClientStorage as IClientStorage',
      () {
        final DpapiClientStorage storage = DpapiClientStorage(
          filePath: 'unused-state-file.dat',
        );

        expect(storage, isA<IClientStorage>());
      },
    );
  });
}
