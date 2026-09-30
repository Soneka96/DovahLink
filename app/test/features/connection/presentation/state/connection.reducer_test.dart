import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
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
      final KnownHost first = Fixtures.buildKnownHost(
        host: Fixtures.buildHost(displayName: 'First Host'),
        availability: HostAvailability.online,
      );
      final KnownHost second = Fixtures.buildKnownHost(
        host: Fixtures.buildHost(displayName: 'Second Host'),
        availability: HostAvailability.offline,
      );
      final ConnectionState state = ConnectionState(
        knownHosts: <KnownHost>[first],
      );

      final ConnectionState result = connectionReducer(
        state,
        ConnectionKnownHostsChangedAction(<KnownHost>[first, second]),
      );

      expect(result.knownHosts, <KnownHost>[first, second]);
      expect(result.knownHostsStatus, KnownHostsObservationStatus.ready);
    });

    test(
      'ConnectionKnownHostsChangedAction clears the SDK projection on an empty collection',
      () {
        final ConnectionState state = ConnectionState(
          knownHosts: <KnownHost>[Fixtures.buildKnownHost()],
        );

        final ConnectionState result = connectionReducer(
          state,
          ConnectionKnownHostsChangedAction(const <KnownHost>[]),
        );

        expect(result.knownHosts, isEmpty);
        expect(result.knownHostsStatus, KnownHostsObservationStatus.ready);
      },
    );

    test(
      'ConnectionKnownHostsChangedAction preserves discovery and candidate selection state',
      () {
        final Host candidate = Fixtures.buildHost(
          hostId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
          displayName: 'Candidate',
        );
        final List<Host> candidates = <Host>[candidate];
        final KnownHost knownHost = Fixtures.buildKnownHost(
          availability: HostAvailability.online,
        );
        final ConnectionState state = ConnectionState(
          hosts: candidates,
          selectedHost: candidate,
          knownHostsStatus: KnownHostsObservationStatus.ready,
        );

        final ConnectionState result = connectionReducer(
          state,
          ConnectionKnownHostsChangedAction(<KnownHost>[knownHost]),
        );

        expect(result.hosts, candidates);
        expect(result.selectedHost, candidate);
        expect(
          result.selectedHostSource,
          ConnectionHostSelectionSource.candidate,
        );
        expect(result.knownHosts, <KnownHost>[knownHost]);
      },
    );

    test(
      'ConnectionKnownHostsChangedAction rebinds a selected candidate to SDK metadata',
      () {
        final Host candidate = Fixtures.buildHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          displayName: 'Candidate metadata',
        );
        final Host authoritativeHost = Fixtures.buildHost(
          hostId: candidate.hostId,
          displayName: 'Current SDK metadata',
          uri: Uri.parse('ws://127.0.0.1:58232/'),
        );
        final ConnectionState state = connectionReducer(
          ConnectionState(
            hosts: <Host>[candidate],
            selectedHost: candidate,
            pendingPairingHostId: candidate.hostId,
          ),
          ConnectionKnownHostsChangedAction(<KnownHost>[
            Fixtures.buildKnownHost(
              host: authoritativeHost,
              availability: HostAvailability.online,
            ),
          ]),
        );

        expect(state.selectedHost, authoritativeHost);
        expect(
          state.selectedHostSource,
          ConnectionHostSelectionSource.knownHost,
        );
        expect(state.pendingPairingHostId, isNull);
      },
    );

    test(
      'ConnectionKnownHostsChangedAction refreshes a selected Host endpoint by ID',
      () {
        final Host selected = Fixtures.buildHost(
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final Host refreshed = Fixtures.buildHost(
          displayName: 'Renamed Host',
          uri: Uri.parse('ws://127.0.0.1:58232/'),
        );
        final ConnectionState result = connectionReducer(
          ConnectionState(
            selectedHost: selected,
            selectedHostSource: ConnectionHostSelectionSource.knownHost,
          ),
          ConnectionKnownHostsChangedAction(<KnownHost>[
            Fixtures.buildKnownHost(host: refreshed),
          ]),
        );

        expect(result.selectedHost, refreshed);
        expect(
          result.selectedHostSource,
          ConnectionHostSelectionSource.knownHost,
        );
      },
    );

    test(
      'ConnectionKnownHostsChangedAction clears a removed Known Host selection',
      () {
        final Host selected = Fixtures.buildHost();
        final ConnectionState result = connectionReducer(
          ConnectionState(
            selectedHost: selected,
            selectedHostSource: ConnectionHostSelectionSource.knownHost,
          ),
          ConnectionKnownHostsChangedAction(<KnownHost>[]),
        );

        expect(result.selectedHost, isNull);
      },
    );
  });

  group('Action ConnectionCandidatesChangedAction behaves correctly', () {
    test('ConnectionCandidatesChangedAction replaces the SDK projection', () {
      final Host first = Fixtures.buildHost();
      final Host second = Fixtures.buildHost(
        hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      );

      final ConnectionState result = connectionReducer(
        ConnectionState(hosts: <Host>[first]),
        ConnectionCandidatesChangedAction(<Host>[second]),
      );

      expect(result.hosts, <Host>[second]);
    });

    test(
      'ConnectionCandidatesChangedAction refreshes a selected candidate after an endpoint change',
      () {
        final Host selected = Fixtures.buildHost(
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final Host refreshed = Fixtures.buildHost(
          uri: Uri.parse('ws://127.0.0.1:58232/'),
        );

        final ConnectionState result = connectionReducer(
          ConnectionState(hosts: <Host>[selected], selectedHost: selected),
          ConnectionCandidatesChangedAction(<Host>[refreshed]),
        );

        expect(result.selectedHost, refreshed);
        expect(
          result.selectedHostSource,
          ConnectionHostSelectionSource.candidate,
        );
      },
    );

    test(
      'ConnectionCandidatesChangedAction clears selection when its candidate disappears',
      () {
        final Host selected = Fixtures.buildHost();
        final ConnectionState result = connectionReducer(
          ConnectionState(hosts: <Host>[selected], selectedHost: selected),
          ConnectionCandidatesChangedAction(<Host>[]),
        );

        expect(result.selectedHost, isNull);
      },
    );

    test(
      'ConnectionCandidatesChangedAction preserves a Known Host selection',
      () {
        final Host selected = Fixtures.buildHost();
        final ConnectionState result = connectionReducer(
          ConnectionState(
            selectedHost: selected,
            selectedHostSource: ConnectionHostSelectionSource.knownHost,
          ),
          ConnectionCandidatesChangedAction(<Host>[]),
        );

        expect(result.selectedHost, selected);
      },
    );

    test(
      'ConnectionCandidatesChangedAction retains pending selection until Known Host confirmation',
      () {
        final Host candidate = Fixtures.buildHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        final ConnectionState pending = connectionReducer(
          ConnectionState(hosts: <Host>[candidate], selectedHost: candidate),
          ConnectionCandidatePairingStartedAction(candidate.hostId),
        );

        final ConnectionState removed = connectionReducer(
          pending,
          ConnectionCandidatesChangedAction(<Host>[]),
        );

        expect(removed.hosts, isEmpty);
        expect(removed.selectedHost, candidate);
        expect(
          removed.selectedHostSource,
          ConnectionHostSelectionSource.candidate,
        );
        expect(removed.pendingPairingHostId, candidate.hostId);
      },
    );

    test(
      'ConnectionCandidatesChangedAction does not promote selection when an unrelated Host becomes known',
      () {
        final Host selectedCandidate = Fixtures.buildHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        final Host unrelatedCandidate = Fixtures.buildHost(
          hostId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        );
        final KnownHost unrelatedKnownHost = Fixtures.buildKnownHost(
          host: unrelatedCandidate,
        );
        final ConnectionState state = ConnectionState(
          hosts: <Host>[selectedCandidate, unrelatedCandidate],
          selectedHost: selectedCandidate,
          pendingPairingHostId: selectedCandidate.hostId,
          knownHosts: <KnownHost>[unrelatedKnownHost],
        );

        final ConnectionState result = connectionReducer(
          state,
          ConnectionCandidatesChangedAction(<Host>[selectedCandidate]),
        );

        expect(result.selectedHost, selectedCandidate);
        expect(
          result.selectedHostSource,
          ConnectionHostSelectionSource.candidate,
        );
        expect(result.pendingPairingHostId, selectedCandidate.hostId);
      },
    );

    test(
      'ConnectionCandidatesChangedAction rebinds to Known Host metadata when it arrives before candidate removal',
      () {
        final Host candidate = Fixtures.buildHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        final Host authoritativeHost = Fixtures.buildHost(
          hostId: candidate.hostId,
          displayName: 'SDK Host Name',
        );
        final ConnectionState pending = connectionReducer(
          ConnectionState(hosts: <Host>[candidate], selectedHost: candidate),
          ConnectionCandidatePairingStartedAction(candidate.hostId),
        );
        final ConnectionState known = connectionReducer(
          pending,
          ConnectionKnownHostsChangedAction(<KnownHost>[
            Fixtures.buildKnownHost(host: authoritativeHost),
          ]),
        );

        final ConnectionState result = connectionReducer(
          known,
          ConnectionCandidatesChangedAction(<Host>[]),
        );

        expect(result.selectedHost, authoritativeHost);
        expect(
          result.selectedHostSource,
          ConnectionHostSelectionSource.knownHost,
        );
        expect(result.pendingPairingHostId, isNull);
      },
    );

    test(
      'ConnectionCandidatesChangedAction rebinds to Known Host metadata when candidate removal arrives first',
      () {
        final Host candidate = Fixtures.buildHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        final Host authoritativeHost = Fixtures.buildHost(
          hostId: candidate.hostId,
          displayName: 'SDK Host Name',
        );
        final ConnectionState pending = connectionReducer(
          ConnectionState(hosts: <Host>[candidate], selectedHost: candidate),
          ConnectionCandidatePairingStartedAction(candidate.hostId),
        );
        final ConnectionState removed = connectionReducer(
          pending,
          ConnectionCandidatesChangedAction(<Host>[]),
        );
        final ConnectionState result = connectionReducer(
          removed,
          ConnectionKnownHostsChangedAction(<KnownHost>[
            Fixtures.buildKnownHost(host: authoritativeHost),
          ]),
        );

        expect(result.selectedHost, authoritativeHost);
        expect(
          result.selectedHostSource,
          ConnectionHostSelectionSource.knownHost,
        );
        expect(result.pendingPairingHostId, isNull);
      },
    );

    test(
      'ConnectionCandidatePairingEndedAction clears a vanished candidate after failure',
      () {
        final Host candidate = Fixtures.buildHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        final ConnectionState pending = connectionReducer(
          ConnectionState(
            selectedHost: candidate,
            pendingPairingHostId: candidate.hostId,
          ),
          ConnectionCandidatesChangedAction(<Host>[]),
        );

        final ConnectionState result = connectionReducer(
          pending,
          ConnectionCandidatePairingEndedAction(candidate.hostId),
        );

        expect(result.selectedHost, isNull);
        expect(result.pendingPairingHostId, isNull);
      },
    );

    test(
      'ConnectionCandidatePairingEndedAction keeps an available candidate after failure',
      () {
        final Host candidate = Fixtures.buildHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        final ConnectionState pending = connectionReducer(
          ConnectionState(hosts: <Host>[candidate], selectedHost: candidate),
          ConnectionCandidatePairingStartedAction(candidate.hostId),
        );

        final ConnectionState result = connectionReducer(
          pending,
          ConnectionCandidatePairingEndedAction(candidate.hostId),
        );

        expect(result.selectedHost, candidate);
        expect(
          result.selectedHostSource,
          ConnectionHostSelectionSource.candidate,
        );
        expect(result.pendingPairingHostId, isNull);
      },
    );

    test('ConnectionCandidatePairingEndedAction ignores a stale Host ID', () {
      final Host currentCandidate = Fixtures.buildHost(
        hostId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
      );
      final ConnectionState state = ConnectionState(
        hosts: <Host>[currentCandidate],
        selectedHost: currentCandidate,
        pendingPairingHostId: currentCandidate.hostId,
      );

      final ConnectionState result = connectionReducer(
        state,
        const ConnectionCandidatePairingEndedAction(
          'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        ),
      );

      expect(result.selectedHost, currentCandidate);
      expect(result.pendingPairingHostId, currentCandidate.hostId);
    });
  });

  group(
    'Action ConnectionKnownHostsObservationFailedAction behaves correctly',
    () {
      test(
        'ConnectionKnownHostsObservationFailedAction preserves the last collection and marks it failed',
        () {
          final List<KnownHost> knownHosts = <KnownHost>[
            Fixtures.buildKnownHost(availability: HostAvailability.online),
          ];
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
      final List<KnownHost> knownHosts = <KnownHost>[
        Fixtures.buildKnownHost(availability: HostAvailability.offline),
      ];
      final ConnectionState initial = ConnectionState(knownHosts: knownHosts);
      final ConnectionState started = connectionReducer(
        initial,
        const ConnectionDiscoveryStartedAction(),
      );
      final ConnectionState succeeded = connectionReducer(
        started,
        const ConnectionDiscoverySucceededAction(hasCandidates: true),
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
      final List<KnownHost> knownHosts = <KnownHost>[
        Fixtures.buildKnownHost(),
        Fixtures.buildKnownHost(
          host: Fixtures.buildHost(displayName: 'Second Host'),
          availability: HostAvailability.online,
        ),
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
        final List<KnownHost> knownHosts = <KnownHost>[
          Fixtures.buildKnownHost(availability: HostAvailability.online),
        ];
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
      'ConnectionDiscoveryStartedAction retains candidates and clears prior failure',
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

        expect(result.hosts, state.hosts);
        expect(result.selectedHost, selectedHost);
        expect(result.discoveryStatus, ConnectionDiscoveryStatus.discovering);
        expect(result.discoveryFailure, isNull);
      },
    );
  });

  group('Action ConnectionDiscoverySucceededAction behaves correctly', () {
    test(
      'ConnectionDiscoverySucceededAction records availability without replacing SDK candidates',
      () {
        final Host latestCandidate = Fixtures.buildHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        final ConnectionState state = ConnectionState(
          hosts: <Host>[latestCandidate],
          selectedHost: latestCandidate,
          discoveryStatus: ConnectionDiscoveryStatus.failed,
          discoveryFailure: ConnectionFailureReason.hostUnavailable,
        );

        final ConnectionState result = connectionReducer(
          state,
          const ConnectionDiscoverySucceededAction(hasCandidates: true),
        );

        expect(result.hosts, <Host>[latestCandidate]);
        expect(result.selectedHost, latestCandidate);
        expect(result.discoveryStatus, ConnectionDiscoveryStatus.available);
        expect(result.discoveryFailure, isNull);
      },
    );

    test(
      'ConnectionDiscoverySucceededAction records empty results without clearing SDK candidates',
      () {
        final Host latestCandidate = Fixtures.buildHost();
        final ConnectionState result = connectionReducer(
          ConnectionState(
            hosts: <Host>[latestCandidate],
            selectedHost: latestCandidate,
          ),
          const ConnectionDiscoverySucceededAction(hasCandidates: false),
        );

        expect(result.hosts, <Host>[latestCandidate]);
        expect(result.selectedHost, latestCandidate);
        expect(result.discoveryStatus, ConnectionDiscoveryStatus.empty);
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

        expect(result.hosts, <Host>[candidate]);
        expect(result.discoveryStatus, ConnectionDiscoveryStatus.failed);
        expect(
          result.discoveryFailure,
          ConnectionFailureReason.hostUnavailable,
        );
        expect(result.selectedHost, candidate);
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
