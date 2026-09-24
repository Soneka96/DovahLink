import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_dialog.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import '../../../../../fixtures/fixtures.dart';

/// Mocktail double for the Redux [Store] the ViewModel reads.
class MockStore extends Mock implements Store<AppState> {}

/// Builds an [AppState] in [phase] with [error] and the selected [host].
AppState buildState({
  PairingPhase phase = PairingPhase.none,
  String? error,
  Host? host,
}) => AppState(
  connection: ConnectionState(selectedHost: host),
  pairing: PairingState(
    phase: phase,
    hostVersion: null,
    error: error,
    codeExpiresAt: null,
    renotifyAvailableAt: null,
  ),
);

/// Reads the title [PairingDialogViewModel.fromStore] derives from [state].
String titleFor(MockStore store, AppState state) {
  when(() => store.state).thenReturn(state);
  return PairingDialogViewModel.fromStore(store).title;
}

/// Exercises [PairingDialogViewModel.fromStore] title derivation.
void main() {
  late MockStore store;
  final Host host = Fixtures.buildHost(displayName: 'Gaming PC');

  setUp(() {
    store = MockStore();
  });

  group('Method fromStore behaves correctly', () {
    test(
      'Method fromStore titles the dialog with the Host name while pairing',
      () {
        for (final PairingPhase phase in const [
          PairingPhase.none,
          PairingPhase.connecting,
          PairingPhase.requestingCode,
          PairingPhase.awaitingCode,
          PairingPhase.confirming,
          PairingPhase.failed,
        ]) {
          expect(
            titleFor(store, buildState(phase: phase, host: host)),
            'Pair with Gaming PC',
            reason: '$phase',
          );
        }
      },
    );

    test(
      'Method fromStore titles a first-time unpaired session with the Host name',
      () {
        expect(
          titleFor(store, buildState(phase: PairingPhase.unpaired, host: host)),
          'Pair with Gaming PC',
        );
      },
    );

    test(
      'Method fromStore titles the dialog Skyrim isn’t running while disconnected',
      () {
        expect(
          titleFor(
            store,
            buildState(phase: PairingPhase.disconnected, host: host),
          ),
          'Skyrim isn’t running',
        );
      },
    );

    test('Method fromStore titles the dialog Connected once trusted', () {
      expect(
        titleFor(store, buildState(phase: PairingPhase.trusted, host: host)),
        'Connected',
      );
    });

    test(
      'Method fromStore titles the dialog Pairing required for a rejected credential',
      () {
        expect(
          titleFor(
            store,
            buildState(
              phase: PairingPhase.unpaired,
              error: "This device's trust was revoked.",
              host: host,
            ),
          ),
          'Pairing required',
        );
      },
    );

    test(
      'Method fromStore keeps the Host title when an error accompanies another phase',
      () {
        expect(
          titleFor(
            store,
            buildState(
              phase: PairingPhase.awaitingCode,
              error: 'That code is not correct.',
              host: host,
            ),
          ),
          'Pair with Gaming PC',
        );
      },
    );

    test(
      'Method fromStore falls back to a generic name with no Host selected',
      () {
        expect(titleFor(store, buildState()), 'Pair with this PC');
      },
    );

    test('Method fromStore dispatches nothing', () {
      when(() => store.state).thenReturn(buildState(host: host));
      when(() => store.dispatch(any())).thenAnswer((_) {});

      PairingDialogViewModel.fromStore(store);

      verifyNever(() => store.dispatch(any()));
    });
  });

  group('Behavior equality behaves correctly', () {
    test('PairingDialogViewModel equals another with the same title', () {
      const PairingDialogViewModel first = PairingDialogViewModel(
        title: 'Connected',
      );
      const PairingDialogViewModel second = PairingDialogViewModel(
        title: 'Connected',
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('PairingDialogViewModel differs when the title differs', () {
      const PairingDialogViewModel first = PairingDialogViewModel(
        title: 'Connected',
      );
      const PairingDialogViewModel second = PairingDialogViewModel(
        title: 'Pairing required',
      );

      expect(first, isNot(second));
      expect(first.hashCode, isNot(second.hashCode));
    });
  });
}
