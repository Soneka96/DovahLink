import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises connection selectors over root application state.
void main() {
  group('ConnectionSelectors', () {
    test('selects the Host list from AppState', () {
      final HostEntity host = Fixtures.buildHostEntity();
      final AppState state = AppState(
        connection: ConnectionState(hosts: [host]),
        pairing: PairingState.initial(),
      );

      expect(ConnectionSelectors.hostsSelector(state), [host]);
    });
  });
}
