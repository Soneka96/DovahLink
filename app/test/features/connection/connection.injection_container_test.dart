import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/connection.injection_container.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkDiscoveryService,
        HostPresenceProbe,
        IDovahLinkDiscoveryService,
        IHostPresenceProbe;

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
      expect(sl<IHostPresenceProbe>(), isA<HostPresenceProbe>());
      expect(sl<IConnectionMiddleware>(), isA<ConnectionMiddleware>());
    });
  });
}
