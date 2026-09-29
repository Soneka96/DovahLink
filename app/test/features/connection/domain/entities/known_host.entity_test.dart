import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises Known Host projection values.
void main() {
  group('Property host behaves correctly', () {
    test('KnownHost.host stores the app-owned Host metadata', () {
      final Host host = Fixtures.buildHost();

      expect(Fixtures.buildKnownHost(host: host).host, host);
    });
  });

  group('Property availability behaves correctly', () {
    test('KnownHost.availability stores the supplied runtime state', () {
      final KnownHost knownHost = Fixtures.buildKnownHost(
        availability: HostAvailability.offline,
      );

      expect(knownHost.availability, HostAvailability.offline);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('KnownHost equality includes Host metadata and availability', () {
      final KnownHost unknown = Fixtures.buildKnownHost();
      final KnownHost online = Fixtures.buildKnownHost(
        availability: HostAvailability.online,
      );
      final KnownHost otherHost = Fixtures.buildKnownHost(
        host: Fixtures.buildHost(displayName: 'Other Host'),
      );

      expect(unknown, isNot(online));
      expect(unknown, isNot(otherHost));

      final KnownHost equal = Fixtures.buildKnownHost();
      expect(unknown, equal);
      expect(unknown.hashCode, equal.hashCode);
    });
  });
}
