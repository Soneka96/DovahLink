import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/unsupported_client_storage.dart';

import '../fixtures/fixtures.dart';

/// Runs unsupported-storage behavior tests.
void main() {
  const UnsupportedClientStorage storage = UnsupportedClientStorage();

  group('Method load behaves correctly', () {
    test('Method load reports unsupported secure storage', () async {
      await expectLater(storage.load(), throwsA(isA<UnsupportedError>()));
    });
  });

  group('Method save behaves correctly', () {
    test('Method save reports unsupported secure storage', () async {
      final PersistedClientState state = Fixtures.buildPersistedClientState();

      await expectLater(storage.save(state), throwsA(isA<UnsupportedError>()));
    });
  });

  group('Method clear behaves correctly', () {
    test('Method clear reports unsupported secure storage', () async {
      await expectLater(storage.clear(), throwsA(isA<UnsupportedError>()));
    });
  });
}
