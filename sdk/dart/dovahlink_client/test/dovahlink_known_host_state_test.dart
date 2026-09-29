import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'fixtures/fixtures.dart';

/// Runs Known Host runtime value behavior tests.
void main() {
  group('Property host behaves correctly', () {
    test('Property host returns the supplied Host metadata', () {
      final DovahLinkHost host = Fixtures.buildDovahLinkHost();
      final DovahLinkKnownHostState state =
          Fixtures.buildDovahLinkKnownHostState(host: host);

      expect(state.host, isA<DovahLinkHost>());
      expect(state.host, host);
    });
  });

  group('Property availability behaves correctly', () {
    test('Property availability returns the supplied runtime state', () {
      final DovahLinkKnownHostState state =
          Fixtures.buildDovahLinkKnownHostState(
            availability: DovahLinkHostAvailability.offline,
          );

      expect(state.availability, isA<DovahLinkHostAvailability>());
      expect(state.availability, DovahLinkHostAvailability.offline);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality compares Host and availability values', () {
      final DovahLinkKnownHostState first =
          Fixtures.buildDovahLinkKnownHostState();
      final DovahLinkKnownHostState equal =
          Fixtures.buildDovahLinkKnownHostState();
      final DovahLinkKnownHostState online =
          Fixtures.buildDovahLinkKnownHostState(
            availability: DovahLinkHostAvailability.online,
          );

      expect(first, equal);
      expect(first.hashCode, equal.hashCode);
      expect(first, isNot(online));
    });
  });
}
