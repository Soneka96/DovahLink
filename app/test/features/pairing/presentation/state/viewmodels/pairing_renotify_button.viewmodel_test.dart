import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_renotify_button.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Mocks the Redux store used to create a pairing redisplay ViewModel.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [PairingRenotifyButtonViewModel.fromStore] projections and callbacks.
void main() {
  late MockStore store;

  setUpAll(() {
    registerFallbackValue(const PairingRenotifyRequestedAction());
  });

  setUp(() {
    store = MockStore();
  });

  group('Method fromStore behaves correctly', () {
    test('Method fromStore allows redisplay when no cooldown is reported', () {
      when(() => store.state).thenReturn(AppState.initial());

      final PairingRenotifyButtonViewModel viewModel =
          PairingRenotifyButtonViewModel.fromStore(store);

      expect(viewModel.isAvailable, isTrue);
      expect(viewModel.cooldownSeconds, isNull);
      expect(viewModel.onPressed, isNotNull);
      verifyNever(() => store.dispatch(any()));
    });

    test('Method fromStore maps an active cooldown and disables redisplay', () {
      when(() => store.state).thenReturn(
        AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            hostVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: DateTime.now().add(const Duration(minutes: 1)),
          ),
        ),
      );

      final PairingRenotifyButtonViewModel viewModel =
          PairingRenotifyButtonViewModel.fromStore(store);

      expect(viewModel.isAvailable, isFalse);
      expect(viewModel.cooldownSeconds, isA<int>());
      expect(viewModel.cooldownSeconds, greaterThan(0));
      expect(viewModel.onPressed, isNull);
      verifyNever(() => store.dispatch(any()));
    });

    test('Method fromStore allows redisplay when cooldown has elapsed', () {
      when(() => store.state).thenReturn(
        AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            hostVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: DateTime.now().subtract(
              const Duration(seconds: 1),
            ),
          ),
        ),
      );

      final PairingRenotifyButtonViewModel viewModel =
          PairingRenotifyButtonViewModel.fromStore(store);

      expect(viewModel.isAvailable, isTrue);
      expect(viewModel.cooldownSeconds, 0);
      expect(viewModel.onPressed, isNotNull);
    });

    test(
      'Method fromStore creates a callback that dispatches a redisplay request',
      () {
        when(() => store.state).thenReturn(AppState.initial());
        when(() => store.dispatch(any())).thenAnswer((_) {});

        final PairingRenotifyButtonViewModel viewModel =
            PairingRenotifyButtonViewModel.fromStore(store);
        viewModel.onPressed!();

        verify(
          () => store.dispatch(const PairingRenotifyRequestedAction()),
        ).called(1);
      },
    );
  });

  group('Method displayLabel behaves correctly', () {
    test('Method displayLabel uses the supplied label when available', () {
      const PairingRenotifyButtonViewModel viewModel =
          PairingRenotifyButtonViewModel(
            isAvailable: true,
            cooldownSeconds: null,
            onPressed: null,
          );

      expect(viewModel.displayLabel(label: 'Send Again'), 'Send Again');
    });

    test('Method displayLabel includes cooldown seconds by default', () {
      const PairingRenotifyButtonViewModel viewModel =
          PairingRenotifyButtonViewModel(
            isAvailable: false,
            cooldownSeconds: 3,
            onPressed: null,
          );

      expect(viewModel.displayLabel(label: 'Send Again'), 'Send Again (3s)');
    });

    test('Method displayLabel uses the custom cooldown label', () {
      const PairingRenotifyButtonViewModel viewModel =
          PairingRenotifyButtonViewModel(
            isAvailable: false,
            cooldownSeconds: 3,
            onPressed: null,
          );

      expect(
        viewModel.displayLabel(
          label: 'Send Again',
          cooldownLabel: 'Please wait',
        ),
        'Please wait',
      );
    });
  });

  group('Behavior equality behaves correctly', () {
    test(
      'Behavior equality ignores callback identity when presentation values match',
      () {
        void firstCallback() {}
        void secondCallback() {}
        final PairingRenotifyButtonViewModel first =
            PairingRenotifyButtonViewModel(
              isAvailable: false,
              cooldownSeconds: 3,
              onPressed: firstCallback,
            );
        final PairingRenotifyButtonViewModel second =
            PairingRenotifyButtonViewModel(
              isAvailable: false,
              cooldownSeconds: 3,
              onPressed: secondCallback,
            );

        expect(first, second);
        expect(first.hashCode, second.hashCode);
      },
    );

    test('Behavior equality differs when cooldown presentation changes', () {
      const PairingRenotifyButtonViewModel first =
          PairingRenotifyButtonViewModel(
            isAvailable: false,
            cooldownSeconds: 3,
            onPressed: null,
          );
      const PairingRenotifyButtonViewModel second =
          PairingRenotifyButtonViewModel(
            isAvailable: false,
            cooldownSeconds: 2,
            onPressed: null,
          );

      expect(first, isNot(second));
    });
  });
}
