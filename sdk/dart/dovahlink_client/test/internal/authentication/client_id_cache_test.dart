import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_cache.dart';

/// Runs client ID cache behavior tests.
void main() {
  group('Property clientId behaves correctly', () {
    test('Property clientId returns null before set is called', () {
      final ClientIdCache cache = ClientIdCache();

      expect(cache.clientId, isNull);
    });
  });

  group('Method set behaves correctly', () {
    test('Method set updates clientId to the given value', () {
      final ClientIdCache cache = ClientIdCache();

      cache.set('client-1');

      expect(cache.clientId, 'client-1');
    });

    test('Method set overwrites a previously cached value', () {
      final ClientIdCache cache = ClientIdCache();
      cache.set('client-1');

      cache.set('client-2');

      expect(cache.clientId, 'client-2');
    });
  });
}
