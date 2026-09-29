import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.reducer.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../fixtures/fixtures.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart'
    show PairingConfirmedAction, PairingSessionTrustedAction;

/// Exercises connection reducer transitions.
void main() {
  group('Action ConnectionKnownHostsChangedAction behaves correctly', () {
    test('ConnectionKnownHostsChangedAction replaces the SDK projection', () {
      final Host first = Fixtures.buildHost(displayName: 'First Host');
      final Host second = Fixtures.buildHost(displayName: 'Second Host');
      final ConnectionState state = ConnectionState(knownHosts: <Host>[first]);

      final ConnectionState result = connectionReducer(
        state,
        ConnectionKnownHostsChangedAction(<Host>[first, second]),
      );

      expect(result.knownHosts, <Host>[first, second]);
      expect(result.knownHostsStatus, KnownHostsObservationStatus.ready);
    });

    test(
      'ConnectionKnownHostsChangedAction clears the SDK projection on null',
      () {
        final ConnectionState state = ConnectionState(
          knownHosts: <Host>[Fixtures.buildHost()],
        );

        final ConnectionState result = connectionReducer(
          state,
          ConnectionKnownHostsChangedAction(const <Host>[]),
        );

        expect(result.knownHosts, isEmpty);
        expect(result.knownHostsStatus, KnownHostsObservationStatus.ready);
      },
    );
  });

  group(
    'Action ConnectionKnownHostsObservationFailedAction behaves correctly',
    () {
      test(
        'ConnectionKnownHostsObservationFailedAction preserves the last collection and marks it failed',
        () {
          final List<Host> knownHosts = <Host>[Fixtures.buildHost()];
          final ConnectionState state = ConnectionState(
            knownHosts: knownHosts,
            knownHostsStatus: KnownHostsObservationStatus.ready,
          );

          final ConnectionState result = connectionReducer(
            state,
            const ConnectionKnownHostsObservationFailedAction(),
          );

          expect(result.knownHosts, knownHosts);
          expect(result.knownHostsStatus, KnownHostsObservationStatus.failed);
        },
      );
    },
  );

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
        expect(result.knownHosts, isEmpty);
      },
    );

    test('Discovery actions do not change the Known Host projection', () {
      final List<Host> knownHosts = <Host>[Fixtures.buildHost()];
      final ConnectionState initial = ConnectionState(knownHosts: knownHosts);
      final ConnectionState started = connectionReducer(
        initial,
        const ConnectionDiscoveryStartedAction(),
      );
      final ConnectionState succeeded = connectionReducer(
        started,
        ConnectionDiscoverySucceededAction([Fixtures.buildHost()]),
      );
      final ConnectionState failed = connectionReducer(
        succeeded,
        const ConnectionDiscoveryFailedAction(
          ConnectionFailureReason.hostUnavailable,
        ),
      );

      expect(started.knownHosts, knownHosts);
      expect(succeeded.knownHosts, knownHosts);
      expect(failed.knownHosts, knownHosts);
    });

    test('PairingSessionTrustedAction alone does not change Known Hosts', () {
      final List<Host> knownHosts = <Host>[
        Fixtures.buildHost(),
        Fixtures.buildHost(displayName: 'Second Host'),
      ];
      final ConnectionState state = ConnectionState(knownHosts: knownHosts);

      final ConnectionState result = connectionReducer(
        state,
        const PairingSessionTrustedAction(),
      );

      expect(result.knownHosts, knownHosts);
    });

    test(
      'PairingConfirmedAction does not change the Known Host projection',
      () {
        final List<Host> knownHosts = <Host>[Fixtures.buildHost()];
        final ConnectionState result = connectionReducer(
          ConnectionState(knownHosts: knownHosts),
          const PairingConfirmedAction(),
        );

        expect(result.knownHosts, knownHosts);
      },
    );
  });

  group('Action ConnectionHostSelectedAction behaves correctly', () {
    test('ConnectionHostSelectedAction stores the selected Host in state', () {
      final Host host = Fixtures.buildHost();

      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        ConnectionHostSelectedAction(
          host,
          source: ConnectionHostSelectionSource.knownHost,
        ),
      );

      expect(result.selectedHost, host);
      expect(
        result.selectedHostSource,
        ConnectionHostSelectionSource.knownHost,
      );
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

    test('ConnectionHostSelectedAction defaults to a candidate source', () {
      final ConnectionState result = connectionReducer(
        ConnectionState.initial(),
        ConnectionHostSelectedAction(Fixtures.buildHost()),
      );

      expect(
        result.selectedHostSource,
        ConnectionHostSelectionSource.candidate,
      );
    });
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

    test(
      'ConnectionDiscoverySucceededAction clears a candidate that disappeared',
      () {
        final Host selected = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final ConnectionState result = connectionReducer(
          ConnectionState(selectedHost: selected),
          const ConnectionDiscoverySucceededAction(<Host>[]),
        );

        expect(result.selectedHost, isNull);
      },
    );

    test(
      'ConnectionDiscoverySucceededAction keeps selection when the endpoint remains',
      () {
        final Host selected = Fixtures.buildHost(
          displayName: 'Before Refresh',
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final Host refreshed = Fixtures.buildHost(
          displayName: 'After Refresh',
          uri: selected.uri,
        );
        final ConnectionState result = connectionReducer(
          ConnectionState(selectedHost: selected),
          ConnectionDiscoverySucceededAction(<Host>[refreshed]),
        );

        expect(result.selectedHost, selected);
      },
    );

    test(
      'ConnectionDiscoverySucceededAction identifies candidates by endpoint instead of display name',
      () {
        final Host selected = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final Host other = Fixtures.buildHost(
          displayName: 'Same Name',
          uri: Uri.parse('ws://127.0.0.1:58232/'),
        );
        final ConnectionState result = connectionReducer(
          ConnectionState(selectedHost: selected),
          ConnectionDiscoverySucceededAction(<Host>[other]),
        );

        expect(result.selectedHost, isNull);
      },
    );

    test(
      'ConnectionDiscoverySucceededAction preserves a Known Host selection after discovery loss',
      () {
        final Host knownHost = Fixtures.buildHost();
        final ConnectionState result = connectionReducer(
          ConnectionState(
            selectedHost: knownHost,
            selectedHostSource: ConnectionHostSelectionSource.knownHost,
          ),
          const ConnectionDiscoverySucceededAction(<Host>[]),
        );

        expect(result.selectedHost, knownHost);
      },
    );
  });

  group('Action ConnectionDiscoveryFailedAction behaves correctly', () {
    test(
      'ConnectionDiscoveryFailedAction stores the semantic failure reason',
      () {
        final Host candidate = Fixtures.buildHost();
        final ConnectionState state = ConnectionState(
          hosts: <Host>[candidate],
          selectedHost: candidate,
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
        expect(result.selectedHost, isNull);
      },
    );

    test(
      'ConnectionDiscoveryFailedAction preserves a Known Host selection',
      () {
        final Host knownHost = Fixtures.buildHost();
        final ConnectionState result = connectionReducer(
          ConnectionState(
            selectedHost: knownHost,
            selectedHostSource: ConnectionHostSelectionSource.knownHost,
          ),
          const ConnectionDiscoveryFailedAction(
            ConnectionFailureReason.hostUnavailable,
          ),
        );

        expect(result.selectedHost, knownHost);
      },
    );
  });
}
