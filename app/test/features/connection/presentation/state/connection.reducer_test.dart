import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.reducer.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises connection reducer transitions.
void main() {
  group('Action ConnectionHostSelectedAction behaves correctly', () {
    test('ConnectionHostSelectedAction stores the selected Host in state', () {
      final Host host = Fixtures.buildHost();

      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        ConnectionHostSelectedAction(host),
      );

      expect(result.selectedHost, host);
    });

    test('ConnectionHostSelectedAction preserves the Host list', () {
      final ConnectionState state = ConnectionState.initial();

      final ConnectionState result = connectionReducer(
        state,
        ConnectionHostSelectedAction(Fixtures.buildHost()),
      );

      expect(result.hosts, state.hosts);
    });

    test('ConnectionHostSelectedAction replaces an earlier selection', () {
      final Host first = Fixtures.buildHost(
        uri: Uri.parse('ws://192.168.1.10:1000/'),
      );
      final Host second = Fixtures.buildHost(
        uri: Uri.parse('ws://192.168.1.11:2000/'),
      );
      final ConnectionState state = ConnectionState.initial();

      final ConnectionState afterFirst = connectionReducer(
        state,
        ConnectionHostSelectedAction(first),
      );
      final ConnectionState afterSecond = connectionReducer(
        afterFirst,
        ConnectionHostSelectedAction(second),
      );

      expect(afterFirst.selectedHost, first);
      expect(afterSecond.selectedHost, second);
    });

    test(
      'ConnectionHostSelectedAction keeps two Hosts with the same display name distinct by URI',
      () {
        final Host first = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://192.168.1.10:1000/'),
        );
        final Host second = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://192.168.1.11:2000/'),
        );

        final ConnectionState result = connectionReducer(
          connectionReducer(
            ConnectionState.initial(),
            ConnectionHostSelectedAction(first),
          ),
          ConnectionHostSelectedAction(second),
        );

        expect(result.selectedHost?.uri, second.uri);
        expect(result.selectedHost, isNot(first));
      },
    );
  });

  group('Action Object behaves correctly', () {
    test('Object returns the same state for an unhandled action', () {
      final ConnectionState state = ConnectionState.initial();

      expect(identical(connectionReducer(state, Object()), state), isTrue);
    });
  });
}
