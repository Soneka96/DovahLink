import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises connection selectors over root application state.
void main() {
  AppState stateWith(
    List<Host> hosts, {
    List<KnownHost> knownHosts = const <KnownHost>[],
    Host? selectedHost,
    ConnectionHostSelectionSource selectedHostSource =
        ConnectionHostSelectionSource.candidate,
    ConnectionDiscoveryStatus discoveryStatus = ConnectionDiscoveryStatus.idle,
    ConnectionFailureReason? discoveryFailure,
  }) => AppState(
    connection: ConnectionState(
      hosts: hosts,
      knownHosts: knownHosts,
      selectedHost: selectedHost,
      selectedHostSource: selectedHostSource,
      discoveryStatus: discoveryStatus,
      discoveryFailure: discoveryFailure,
    ),
    pairing: PairingState.initial(),
  );

  group('Selector hostsSelector behaves correctly', () {
    test('Selector hostsSelector selects the Host list from AppState', () {
      final Host host = Fixtures.buildHost();

      expect(ConnectionSelectors.hostsSelector(stateWith([host])), [host]);
    });
  });

  group('Selector discoveryStatusSelector behaves correctly', () {
    test(
      'discoveryStatusSelector selects the current Host discovery state',
      () {
        expect(
          ConnectionSelectors.discoveryStatusSelector(
            stateWith(
              const <Host>[],
              discoveryStatus: ConnectionDiscoveryStatus.discovering,
            ),
          ),
          ConnectionDiscoveryStatus.discovering,
        );
      },
    );
  });

  group('Selector canDiscoverSelector behaves correctly', () {
    test('Selector canDiscoverSelector allows idle discovery', () {
      expect(
        ConnectionSelectors.canDiscoverSelector(
          stateWith(
            const <Host>[],
            discoveryStatus: ConnectionDiscoveryStatus.idle,
          ),
        ),
        isTrue,
      );
    });

    test('Selector canDiscoverSelector rejects discovering state', () {
      expect(
        ConnectionSelectors.canDiscoverSelector(
          stateWith(
            const <Host>[],
            discoveryStatus: ConnectionDiscoveryStatus.discovering,
          ),
        ),
        isFalse,
      );
    });

    test('Selector canDiscoverSelector allows available state', () {
      expect(
        ConnectionSelectors.canDiscoverSelector(
          stateWith([
            Fixtures.buildHost(),
          ], discoveryStatus: ConnectionDiscoveryStatus.available),
        ),
        isTrue,
      );
    });

    test('Selector canDiscoverSelector allows empty state', () {
      expect(
        ConnectionSelectors.canDiscoverSelector(
          stateWith(
            const <Host>[],
            discoveryStatus: ConnectionDiscoveryStatus.empty,
          ),
        ),
        isTrue,
      );
    });

    test('Selector canDiscoverSelector allows failed state', () {
      expect(
        ConnectionSelectors.canDiscoverSelector(
          stateWith(
            const <Host>[],
            discoveryStatus: ConnectionDiscoveryStatus.failed,
          ),
        ),
        isTrue,
      );
    });
  });

  group('Selector discoveryFailureSelector behaves correctly', () {
    test(
      'discoveryFailureSelector selects the semantic reason for presentation',
      () {
        expect(
          ConnectionSelectors.discoveryFailureSelector(
            stateWith(
              const <Host>[],
              discoveryFailure: ConnectionFailureReason.invalidResponse,
            ),
          ),
          ConnectionFailureReason.invalidResponse,
        );
      },
    );
  });

  group('Selector selectedHostSelector behaves correctly', () {
    test('Selector selectedHostSelector returns null before any selection', () {
      expect(
        ConnectionSelectors.selectedHostSelector(
          stateWith([Fixtures.buildHost()]),
        ),
        isNull,
      );
    });

    test('Selector selectedHostSelector returns the selected Host', () {
      final Host first = Fixtures.buildHost(
        displayName: 'Same Name',
        uri: Uri.parse('ws://192.168.1.10:1000/'),
      );
      final Host second = Fixtures.buildHost(
        displayName: 'Same Name',
        uri: Uri.parse('ws://192.168.1.11:2000/'),
      );

      final Host? selected = ConnectionSelectors.selectedHostSelector(
        stateWith([first, second], selectedHost: second),
      );

      expect(selected, second);
      expect(selected?.uri, second.uri);
    });
  });

  group('Selector selectedHostSourceSelector behaves correctly', () {
    test(
      'Selector selectedHostSourceSelector returns the durable Known Host source',
      () {
        expect(
          ConnectionSelectors.selectedHostSourceSelector(
            stateWith(
              [Fixtures.buildHost()],
              selectedHost: Fixtures.buildHost(),
              selectedHostSource: ConnectionHostSelectionSource.knownHost,
            ),
          ),
          ConnectionHostSelectionSource.knownHost,
        );
      },
    );
  });

  group('Selector selectedHostNameSelector behaves correctly', () {
    test(
      'Selector selectedHostNameSelector returns null before any selection',
      () {
        expect(
          ConnectionSelectors.selectedHostNameSelector(
            stateWith([Fixtures.buildHost()]),
          ),
          isNull,
        );
      },
    );

    test(
      'Selector selectedHostNameSelector returns the selected Host name',
      () {
        final Host host = Fixtures.buildHost(displayName: 'Bedroom PC');

        final String? name = ConnectionSelectors.selectedHostNameSelector(
          stateWith([host], selectedHost: host),
        );

        expect(name, isA<String>());
        expect(name, 'Bedroom PC');
      },
    );
  });

  group('Selector hostCardsSelector behaves correctly', () {
    test('Selector hostCardsSelector maps a candidate to its display data', () {
      final Host host = Fixtures.buildHost();

      expect(ConnectionSelectors.hostCardsSelector(stateWith([host])), [
        Fixtures.buildHostCardViewData(host: host),
      ]);
    });

    test(
      'Selector hostCardsSelector includes a Known Host before discovery',
      () {
        final KnownHost knownHost = Fixtures.buildKnownHost(
          availability: HostAvailability.unknown,
        );

        final List<HostCardViewData> cards =
            ConnectionSelectors.hostCardsSelector(
              stateWith([], knownHosts: [knownHost]),
            );

        expect(cards, hasLength(1));
        expect(cards.single.host, knownHost.host);
        expect(cards.single.source, ConnectionHostSelectionSource.knownHost);
        expect(cards.single.state, DovahConnectionCardState.unknown);
        expect(cards.single.subtitle, 'Known Host');
      },
    );

    test('Selector hostCardsSelector maps every Known Host availability', () {
      for (final (
            HostAvailability availability,
            DovahConnectionCardState cardState,
          )
          in const [
            (HostAvailability.checking, DovahConnectionCardState.checking),
            (HostAvailability.unknown, DovahConnectionCardState.unknown),
            (HostAvailability.online, DovahConnectionCardState.available),
            (HostAvailability.offline, DovahConnectionCardState.offline),
          ]) {
        final KnownHost knownHost = Fixtures.buildKnownHost(
          availability: availability,
        );

        final HostCardViewData card = ConnectionSelectors.hostCardsSelector(
          stateWith([], knownHosts: [knownHost]),
        ).single;

        expect(card.host, knownHost.host);
        expect(card.source, ConnectionHostSelectionSource.knownHost);
        expect(card.state, cardState);
      }
    });

    test(
      'Selector hostCardsSelector gives the exact Known Host session phase priority over availability',
      () {
        for (final (
              KnownHostSessionState sessionState,
              HostAvailability availability,
              DovahConnectionCardState cardState,
            )
            in const [
              (
                KnownHostSessionState.connecting,
                HostAvailability.offline,
                DovahConnectionCardState.connecting,
              ),
              (
                KnownHostSessionState.connected,
                HostAvailability.offline,
                DovahConnectionCardState.connected,
              ),
              (
                KnownHostSessionState.reconnecting,
                HostAvailability.online,
                DovahConnectionCardState.reconnecting,
              ),
              (
                KnownHostSessionState.reauthenticating,
                HostAvailability.unknown,
                DovahConnectionCardState.reconnecting,
              ),
              (
                KnownHostSessionState.disconnected,
                HostAvailability.online,
                DovahConnectionCardState.available,
              ),
            ]) {
          final KnownHost knownHost = Fixtures.buildKnownHost(
            sessionState: sessionState,
            availability: availability,
          );

          final HostCardViewData card = ConnectionSelectors.hostCardsSelector(
            stateWith([], knownHosts: [knownHost]),
          ).single;

          expect(card.state, cardState);
          expect(card.source, ConnectionHostSelectionSource.knownHost);
        }
      },
    );

    test(
      'Selector hostCardsSelector passes through the SDK candidate projection',
      () {
        final Host knownHostHost = Fixtures.buildHost(
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final Host candidate = Fixtures.buildHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
          uri: knownHostHost.uri,
        );
        final Host unrelatedCandidate = Fixtures.buildHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
        );
        final KnownHost knownHost = Fixtures.buildKnownHost(
          host: knownHostHost,
          availability: HostAvailability.online,
        );

        final List<HostCardViewData> cards =
            ConnectionSelectors.hostCardsSelector(
              stateWith(
                [candidate, unrelatedCandidate],
                knownHosts: [knownHost],
              ),
            );

        expect(cards, hasLength(3));
        expect(cards.map((HostCardViewData card) => card.host), [
          knownHostHost,
          candidate,
          unrelatedCandidate,
        ]);
        expect(cards.map((HostCardViewData card) => card.source), [
          ConnectionHostSelectionSource.knownHost,
          ConnectionHostSelectionSource.candidate,
          ConnectionHostSelectionSource.candidate,
        ]);
        expect(cards[1].state, DovahConnectionCardState.unknown);
      },
    );

    test(
      'Selector hostCardsSelector keeps Known Hosts visible after discovery clears or fails',
      () {
        final KnownHost knownHost = Fixtures.buildKnownHost();

        for (final ConnectionDiscoveryStatus status in [
          ConnectionDiscoveryStatus.empty,
          ConnectionDiscoveryStatus.failed,
        ]) {
          final List<HostCardViewData> cards =
              ConnectionSelectors.hostCardsSelector(
                stateWith([], knownHosts: [knownHost], discoveryStatus: status),
              );

          expect(cards, hasLength(1));
          expect(cards.single.host, knownHost.host);
          expect(cards.single.source, ConnectionHostSelectionSource.knownHost);
        }
      },
    );

    test(
      'Selector hostCardsSelector marks every card unknown because reachability is not known',
      () {
        final AppState state = stateWith([
          Fixtures.buildHost(),
          Fixtures.buildHost(displayName: 'Second Host'),
        ]);

        final List<DovahConnectionCardState> states =
            ConnectionSelectors.hostCardsSelector(
              state,
            ).map((HostCardViewData card) => card.state).toList();

        expect(states, [
          DovahConnectionCardState.unknown,
          DovahConnectionCardState.unknown,
        ]);
      },
    );

    test('Selector hostCardsSelector keeps Host order and each Host', () {
      final Host first = Fixtures.buildHost(
        displayName: 'First Host',
        uri: Uri.parse('ws://192.168.1.10:1000/'),
      );
      final Host second = Fixtures.buildHost(
        displayName: 'Second Host',
        uri: Uri.parse('ws://192.168.1.11:2000/'),
      );

      final List<HostCardViewData> cards =
          ConnectionSelectors.hostCardsSelector(stateWith([first, second]));

      expect(cards.map((HostCardViewData card) => card.host).toList(), [
        first,
        second,
      ]);
      expect(cards.map((HostCardViewData card) => card.title).toList(), [
        'First Host',
        'Second Host',
      ]);
      expect(cards.map((HostCardViewData card) => card.detail).toList(), [
        '192.168.1.10:1000',
        '192.168.1.11:2000',
      ]);
    });

    test(
      'Selector hostCardsSelector returns no cards when there are no Hosts',
      () {
        expect(ConnectionSelectors.hostCardsSelector(stateWith([])), isEmpty);
      },
    );

    test(
      'Selector hostCardsSelector falls back to the whole endpoint when it has no authority',
      () {
        final Host host = Fixtures.buildHost(uri: Uri.parse('local-host'));

        final HostCardViewData card = ConnectionSelectors.hostCardsSelector(
          stateWith([host]),
        ).single;

        expect(card.detail, isA<String>());
        expect(card.detail, 'local-host');
      },
    );

    test(
      'Selector hostCardsSelector uses the whole endpoint for a Known Host without authority',
      () {
        final KnownHost knownHost = Fixtures.buildKnownHost(
          host: Fixtures.buildHost(uri: Uri.parse('local-host')),
        );

        final HostCardViewData card = ConnectionSelectors.hostCardsSelector(
          stateWith([], knownHosts: [knownHost]),
        ).single;

        expect(card.source, ConnectionHostSelectionSource.knownHost);
        expect(card.detail, 'local-host');
      },
    );

    test('Selector hostCardsSelector keeps a very long Host name intact', () {
      final String longName = 'A very long Host name ' * 12;
      final Host host = Fixtures.buildHost(displayName: longName);

      final HostCardViewData card = ConnectionSelectors.hostCardsSelector(
        stateWith([host]),
      ).single;

      expect(card.title, isA<String>());
      expect(card.title, longName);
    });
  });
}
