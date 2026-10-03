import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../fixtures/fixtures.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show AdministrativeInvalidationReason;

/// Exercises the values carried by connection discovery actions.
void main() {
  group(
    'Behavior equality in ConnectionHostSelectedAction behaves correctly',
    () {
      test('ConnectionHostSelectedAction includes its selection source', () {
        final Host host = Fixtures.buildHost();
        const ConnectionHostSelectionSource candidate =
            ConnectionHostSelectionSource.candidate;
        const ConnectionHostSelectionSource knownHost =
            ConnectionHostSelectionSource.knownHost;

        expect(
          ConnectionHostSelectedAction(host),
          isNot(ConnectionHostSelectedAction(host, source: knownHost)),
        );
        expect(ConnectionHostSelectedAction(host).source, candidate);
      });
    },
  );

  group(
    'Behavior equality in ConnectionHostReentryRequestedAction behaves correctly',
    () {
      test(
        'ConnectionHostReentryRequestedAction carries its Known Host ID',
        () {
          const ConnectionHostReentryRequestedAction action =
              ConnectionHostReentryRequestedAction('host-1');
          const ConnectionHostReentryRequestedAction sameAction =
              ConnectionHostReentryRequestedAction('host-1');

          expect(action.hostId, 'host-1');
          expect(action, sameAction);
          expect(action.hashCode, sameAction.hashCode);
        },
      );
    },
  );

  group(
    'Behavior equality in ConnectionKnownHostInvalidatedAction behaves correctly',
    () {
      test('ConnectionKnownHostInvalidatedAction carries Host and reason', () {
        const ConnectionKnownHostInvalidatedAction action =
            ConnectionKnownHostInvalidatedAction(
              hostId: 'host-a',
              reason: AdministrativeInvalidationReason.trustReset,
            );
        const ConnectionKnownHostInvalidatedAction sameAction =
            ConnectionKnownHostInvalidatedAction(
              hostId: 'host-a',
              reason: AdministrativeInvalidationReason.trustReset,
            );

        expect(action.hostId, 'host-a');
        expect(action.reason, AdministrativeInvalidationReason.trustReset);
        expect(action, sameAction);
        expect(action.hashCode, sameAction.hashCode);
      });
    },
  );

  group('Behavior equality in candidate pairing actions behaves correctly', () {
    test('pairing actions carry their candidate Host ID', () {
      const ConnectionCandidatePairingStartedAction started =
          ConnectionCandidatePairingStartedAction('host-1');
      const ConnectionCandidatePairingStartedAction sameStarted =
          ConnectionCandidatePairingStartedAction('host-1');
      const ConnectionCandidatePairingEndedAction ended =
          ConnectionCandidatePairingEndedAction('host-1');
      const ConnectionCandidatePairingEndedAction sameEnded =
          ConnectionCandidatePairingEndedAction('host-1');

      expect(started.hostId, 'host-1');
      expect(started, sameStarted);
      expect(started.hashCode, sameStarted.hashCode);
      expect(ended.hostId, 'host-1');
      expect(ended, sameEnded);
      expect(ended.hashCode, sameEnded.hashCode);
    });
  });

  group(
    'Behavior equality in ConnectionKnownHostsChangedAction behaves correctly',
    () {
      test(
        'ConnectionKnownHostsChangedAction values compare by complete Known Host projection',
        () {
          final List<KnownHost> hosts = <KnownHost>[
            Fixtures.buildKnownHost(availability: HostAvailability.online),
          ];
          final ConnectionKnownHostsChangedAction first =
              ConnectionKnownHostsChangedAction(hosts);
          final ConnectionKnownHostsChangedAction second =
              ConnectionKnownHostsChangedAction(<KnownHost>[
                Fixtures.buildKnownHost(availability: HostAvailability.online),
              ]);

          expect(first, second);
          expect(first.hashCode, second.hashCode);
          expect(
            ConnectionKnownHostsChangedAction(const <KnownHost>[]),
            isNot(first),
          );
          expect(
            () => first.knownHosts.add(Fixtures.buildKnownHost()),
            throwsUnsupportedError,
          );
        },
      );
    },
  );

  group(
    'Behavior equality in ConnectionKnownHostsObservationFailedAction behaves correctly',
    () {
      test(
        'ConnectionKnownHostsObservationFailedAction compares equal without carrying raw errors',
        () {
          const ConnectionKnownHostsObservationFailedAction first =
              ConnectionKnownHostsObservationFailedAction();
          const ConnectionKnownHostsObservationFailedAction second =
              ConnectionKnownHostsObservationFailedAction();

          expect(first, second);
          expect(first.hashCode, second.hashCode);
          expect(first.props, isEmpty);
        },
      );
    },
  );

  group(
    'Behavior equality in ConnectionCandidatesChangedAction behaves correctly',
    () {
      test(
        'ConnectionCandidatesChangedAction carries an immutable complete projection',
        () {
          final List<Host> hosts = <Host>[Fixtures.buildHost()];
          final ConnectionCandidatesChangedAction action =
              ConnectionCandidatesChangedAction(hosts);

          expect(action.hosts, hosts);
          expect(
            () => action.hosts.add(Fixtures.buildHost()),
            throwsUnsupportedError,
          );
        },
      );
    },
  );

  group(
    'Behavior equality in ConnectionDiscoveryRequestedAction behaves correctly',
    () {
      test('ConnectionDiscoveryRequestedAction values compare equal', () {
        const ConnectionDiscoveryRequestedAction first =
            ConnectionDiscoveryRequestedAction();
        const ConnectionDiscoveryRequestedAction second =
            ConnectionDiscoveryRequestedAction();

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      });
    },
  );

  group(
    'Behavior equality in ConnectionDiscoveryStartedAction behaves correctly',
    () {
      test('ConnectionDiscoveryStartedAction values compare equal', () {
        const ConnectionDiscoveryStartedAction first =
            ConnectionDiscoveryStartedAction();
        const ConnectionDiscoveryStartedAction second =
            ConnectionDiscoveryStartedAction();

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      });
    },
  );

  group(
    'Property hasCandidates in ConnectionDiscoverySucceededAction behaves correctly',
    () {
      test(
        'ConnectionDiscoverySucceededAction records only candidate presence',
        () {
          const ConnectionDiscoverySucceededAction available =
              ConnectionDiscoverySucceededAction(hasCandidates: true);
          const ConnectionDiscoverySucceededAction empty =
              ConnectionDiscoverySucceededAction(hasCandidates: false);

          expect(available.hasCandidates, isTrue);
          expect(empty.hasCandidates, isFalse);
        },
      );
    },
  );

  group(
    'Property failure in ConnectionDiscoveryFailedAction behaves correctly',
    () {
      test(
        'ConnectionDiscoveryFailedAction carries the semantic failure reason',
        () {
          const ConnectionDiscoveryFailedAction action =
              ConnectionDiscoveryFailedAction(
                ConnectionFailureReason.hostUnavailable,
              );

          expect(action.failure, ConnectionFailureReason.hostUnavailable);
        },
      );
    },
  );
}
