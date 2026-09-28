import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.reducer.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises connection reducer transitions.
void main() {
  group('Action ConnectionKnownHostChangedAction behaves correctly', () {
    test('ConnectionKnownHostChangedAction replaces the SDK projection', () {
      final Host first = Fixtures.buildHost(displayName: 'First Host');
      final Host second = Fixtures.buildHost(displayName: 'Second Host');
      final ConnectionState state = ConnectionState(knownHost: first);

      final ConnectionState result = connectionReducer(
        state,
        ConnectionKnownHostChangedAction(second),
      );

      expect(result.knownHost, second);
    });

    test(
      'ConnectionKnownHostChangedAction clears the SDK projection on null',
      () {
        final ConnectionState state = ConnectionState(
          knownHost: Fixtures.buildHost(),
        );

        final ConnectionState result = connectionReducer(
          state,
          const ConnectionKnownHostChangedAction(null),
        );

        expect(result.knownHost, isNull);
      },
    );
  });

  group('Action lifecycle inference behaves correctly', () {
    test(
      'PairingConfirmedAction alone does not create a Known Host projection',
      () {
        final ConnectionState state = ConnectionState.initial();

        final ConnectionState result = connectionReducer(
          state,
          const PairingConfirmedAction(),
        );

        expect(identical(result, state), isTrue);
        expect(result.knownHost, isNull);
      },
    );

    test('Discovery success alone does not create a Known Host projection', () {
      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        ConnectionDiscoverySucceededAction([Fixtures.buildHost()]),
      );

      expect(result.knownHost, isNull);
    });
  });

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
      final List<Host> hosts = [
        Fixtures.buildHost(),
        Fixtures.buildHost(
          displayName: 'Second Host',
          uri: Uri.parse('ws://192.168.1.11:58231/'),
        ),
      ];
      final ConnectionState state = ConnectionState(hosts: hosts);

      final ConnectionState result = connectionReducer(
        state,
        ConnectionHostSelectedAction(Fixtures.buildHost()),
      );

      expect(result.hosts, hosts);
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

  group('Action ConnectionDiscoveryRequestedAction behaves correctly', () {
    test(
      'ConnectionDiscoveryRequestedAction leaves lifecycle state unchanged',
      () {
        final ConnectionState state = ConnectionState(
          hosts: [Fixtures.buildHost()],
          discoveryStatus: ConnectionDiscoveryStatus.failed,
          discoveryFailure: ConnectionFailureReason.hostUnavailable,
        );

        final ConnectionState result = connectionReducer(
          state,
          const ConnectionDiscoveryRequestedAction(),
        );

        expect(identical(result, state), isTrue);
      },
    );
  });

  group('Action ConnectionDiscoveryStartedAction behaves correctly', () {
    test(
      'ConnectionDiscoveryStartedAction clears candidates and prior failure',
      () {
        final Host selectedHost = Fixtures.buildHost(displayName: 'Selected');
        final ConnectionState state = ConnectionState(
          hosts: [Fixtures.buildHost()],
          selectedHost: selectedHost,
          discoveryStatus: ConnectionDiscoveryStatus.failed,
          discoveryFailure: ConnectionFailureReason.hostUnavailable,
        );

        final ConnectionState result = connectionReducer(
          state,
          const ConnectionDiscoveryStartedAction(),
        );

        expect(result.hosts, isEmpty);
        expect(result.selectedHost, selectedHost);
        expect(result.discoveryStatus, ConnectionDiscoveryStatus.discovering);
        expect(result.discoveryFailure, isNull);
      },
    );
  });

  group('Action ConnectionDiscoverySucceededAction behaves correctly', () {
    test(
      'ConnectionDiscoverySucceededAction stores every candidate in order',
      () {
        const ConnectionState state = ConnectionState(
          discoveryStatus: ConnectionDiscoveryStatus.failed,
          discoveryFailure: ConnectionFailureReason.hostUnavailable,
        );
        final List<Host> hosts = [
          Fixtures.buildHost(),
          Fixtures.buildHost(
            displayName: 'Second Host',
            uri: Uri.parse('ws://192.168.1.11:58231/'),
          ),
        ];

        final ConnectionState result = connectionReducer(
          state,
          ConnectionDiscoverySucceededAction(hosts),
        );

        expect(result.hosts, hosts);
        expect(result.discoveryStatus, ConnectionDiscoveryStatus.available);
        expect(result.discoveryFailure, isNull);
      },
    );

    test(
      'ConnectionDiscoverySucceededAction records an empty discovery result',
      () {
        final ConnectionState result = connectionReducer(
          ConnectionState.initial(),
          const ConnectionDiscoverySucceededAction(<Host>[]),
        );

        expect(result.hosts, isEmpty);
        expect(result.discoveryStatus, ConnectionDiscoveryStatus.empty);
      },
    );
  });

  group('Action ConnectionDiscoveryFailedAction behaves correctly', () {
    test(
      'ConnectionDiscoveryFailedAction stores the semantic failure reason',
      () {
        final ConnectionState state = ConnectionState(
          hosts: [Fixtures.buildHost()],
          discoveryStatus: ConnectionDiscoveryStatus.available,
        );
        final ConnectionState result = connectionReducer(
          state,
          const ConnectionDiscoveryFailedAction(
            ConnectionFailureReason.hostUnavailable,
          ),
        );

        expect(result.hosts, isEmpty);
        expect(result.discoveryStatus, ConnectionDiscoveryStatus.failed);
        expect(
          result.discoveryFailure,
          ConnectionFailureReason.hostUnavailable,
        );
      },
    );
  });
}
