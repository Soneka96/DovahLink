import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises connection-state initialization and copying.
void main() {
  group('ConnectionState — initial', () {
    test('creates an idle state without discovered Hosts', () {
      final ConnectionState state = ConnectionState.initial();

      expect(state.hosts, isEmpty);
      expect(state.discoveryStatus, ConnectionDiscoveryStatus.idle);
    });

    test('ConnectionState initial has no selected Host', () {
      final ConnectionState state = ConnectionState.initial();

      expect(state.selectedHost, isNull);
    });

    test('ConnectionState initial has no SDK Known Host projection', () {
      expect(ConnectionState.initial().knownHosts, isEmpty);
    });

    test('ConnectionState initial waits for a Known Hosts observation', () {
      expect(
        ConnectionState.initial().knownHostsStatus,
        KnownHostsObservationStatus.loading,
      );
    });
  });

  group('ConnectionState — copyWith', () {
    test('preserves hosts when omitted', () {
      final ConnectionState state = ConnectionState.initial();

      final ConnectionState result = state.copyWith();

      expect(result.hosts, state.hosts);
    });

    test('replaces hosts when supplied', () {
      final ConnectionState state = ConnectionState.initial();
      final List<Host> replacement = [
        Fixtures.buildHost(displayName: 'Other Host'),
      ];

      final ConnectionState result = state.copyWith(hosts: replacement);

      expect(result.hosts, replacement);
    });

    test('ConnectionState copyWith preserves selectedHost when omitted', () {
      final Host host = Fixtures.buildHost();
      final ConnectionState state = ConnectionState(selectedHost: host);

      final ConnectionState result = state.copyWith();

      expect(result.selectedHost, host);
    });

    test('ConnectionState copyWith replaces selectedHost when set', () {
      final Host host = Fixtures.buildHost();

      final ConnectionState result = ConnectionState.initial().copyWith(
        selectedHost: Some(host),
      );

      expect(result.selectedHost, host);
    });

    test('ConnectionState copyWith clears selectedHost when None', () {
      final ConnectionState state = ConnectionState(
        selectedHost: Fixtures.buildHost(),
      );

      final ConnectionState result = state.copyWith(selectedHost: const None());

      expect(result.selectedHost, isNull);
    });

    test(
      'ConnectionState copyWith replaces the full SDK Known Hosts projection',
      () {
        final Host host = Fixtures.buildHost();
        final ConnectionState set = ConnectionState.initial().copyWith(
          knownHosts: <Host>[host],
        );
        final ConnectionState cleared = set.copyWith(
          knownHosts: const <Host>[],
        );

        expect(set.knownHosts, <Host>[host]);
        expect(cleared.knownHosts, isEmpty);
      },
    );

    test(
      'ConnectionState copyWith replaces Known Hosts observation status',
      () {
        final ConnectionState result = ConnectionState.initial().copyWith(
          knownHostsStatus: KnownHostsObservationStatus.failed,
        );

        expect(result.knownHostsStatus, KnownHostsObservationStatus.failed);
      },
    );

    test(
      'ConnectionState copyWith preserves Known Hosts observation status',
      () {
        const ConnectionState state = ConnectionState(
          knownHostsStatus: KnownHostsObservationStatus.failed,
        );

        expect(
          state.copyWith().knownHostsStatus,
          KnownHostsObservationStatus.failed,
        );
      },
    );
  });

  group('ConnectionState — equality', () {
    test('ConnectionState differs when only selectedHost differs', () {
      final ConnectionState unselected = ConnectionState.initial();
      final ConnectionState selected = unselected.copyWith(
        selectedHost: Some(Fixtures.buildHost()),
      );

      expect(selected, isNot(unselected));
    });

    test('ConnectionState differs when only Known Hosts status differs', () {
      const ConnectionState loading = ConnectionState();
      const ConnectionState failed = ConnectionState(
        knownHostsStatus: KnownHostsObservationStatus.failed,
      );

      expect(failed, isNot(loading));
    });
  });

  group('Property discoveryStatus in ConnectionState behaves correctly', () {
    test('ConnectionState starts discovery in idle state', () {
      expect(
        ConnectionState.initial().discoveryStatus,
        ConnectionDiscoveryStatus.idle,
      );
    });

    test('ConnectionState copyWith replaces discovery status', () {
      final ConnectionState result = ConnectionState.initial().copyWith(
        discoveryStatus: ConnectionDiscoveryStatus.discovering,
      );

      expect(result.discoveryStatus, ConnectionDiscoveryStatus.discovering);
    });

    test('ConnectionState copyWith preserves discovery state when omitted', () {
      const ConnectionState state = ConnectionState(
        discoveryStatus: ConnectionDiscoveryStatus.failed,
        discoveryFailure: ConnectionFailureReason.incompatibleHost,
      );

      final ConnectionState result = state.copyWith();

      expect(result.discoveryStatus, ConnectionDiscoveryStatus.failed);
      expect(result.discoveryFailure, ConnectionFailureReason.incompatibleHost);
    });
  });

  group('Property discoveryFailure in ConnectionState behaves correctly', () {
    test('ConnectionState has no failure reason before a failure occurs', () {
      expect(ConnectionState.initial().discoveryFailure, isNull);
    });

    test('ConnectionState copyWith sets and clears the semantic failure', () {
      final ConnectionState failed = ConnectionState.initial().copyWith(
        discoveryFailure: const Some(ConnectionFailureReason.hostUnavailable),
      );
      final ConnectionState cleared = failed.copyWith(
        discoveryFailure: const None(),
      );

      expect(failed.discoveryFailure, ConnectionFailureReason.hostUnavailable);
      expect(cleared.discoveryFailure, isNull);
    });
  });
}
