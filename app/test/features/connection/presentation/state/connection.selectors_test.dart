import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises connection selectors over root application state.
void main() {
  AppState stateWith(List<Host> hosts, {Host? selectedHost}) => AppState(
    connection: ConnectionState(hosts: hosts, selectedHost: selectedHost),
    pairing: PairingState.initial(),
  );

  group('Selector hostsSelector behaves correctly', () {
    test('Selector hostsSelector selects the Host list from AppState', () {
      final Host host = Fixtures.buildHost();

      expect(ConnectionSelectors.hostsSelector(stateWith([host])), [host]);
    });
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
    test('Selector hostCardsSelector maps a Host to its display data', () {
      final Host host = Fixtures.buildHost();

      expect(ConnectionSelectors.hostCardsSelector(stateWith([host])), [
        Fixtures.buildHostCardViewData(host: host),
      ]);
    });

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
