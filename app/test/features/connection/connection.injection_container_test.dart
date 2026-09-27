import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/connection.injection_container.dart';
import 'package:dovahlink_client/injection_container.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkDiscoveryService, IDovahLinkDiscoveryService;

/// Exercises connection feature dependency registration.
void main() {
  setUp(() async {
    await sl.reset();
  });

  tearDown(() async {
    await sl.reset();
  });

  group('Function initConnectionDependencies behaves correctly', () {
    test('initConnectionDependencies registers the SDK discovery contract', () {
      initConnectionDependencies();

      expect(
        sl<IDovahLinkDiscoveryService>(),
        isA<DovahLinkDiscoveryService>(),
      );
    });
  });
}
