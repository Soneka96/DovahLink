import 'package:flutter/material.dart' hide ConnectionState;
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_renotify_button.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises [PairingRenotifyButton] dispatch behavior and enabled/disabled states.
void main() {
  group('PairingRenotifyButton', () {
    testWidgets('PairingRenotifyButton displays enabled button when renotify is available', (
      WidgetTester tester,
    ) async {
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingRenotifyButton()),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Send Code Again');
    });

    testWidgets('PairingRenotifyButton displays disabled button during cooldown', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final availableIn5Seconds = now.add(const Duration(seconds: 5));
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: availableIn5Seconds,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingRenotifyButton()),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('PairingRenotifyButton shows cooldown seconds in default format', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final availableIn3Seconds = now.add(const Duration(seconds: 3));
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: availableIn3Seconds,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingRenotifyButton()),
          ),
        ),
      );

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, matches(r'Send Code Again \(\d+s\)'));
    });

    testWidgets('PairingRenotifyButton treats zero cooldown seconds as available', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final alreadyElapsed = now.subtract(const Duration(seconds: 1));
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: alreadyElapsed,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingRenotifyButton()),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Send Code Again');
    });

    testWidgets('PairingRenotifyButton uses custom cooldown label when provided', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final availableIn5Seconds = now.add(const Duration(seconds: 5));
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: availableIn5Seconds,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: PairingRenotifyButton(cooldownLabel: 'Please wait...'),
            ),
          ),
        ),
      );

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Please wait...');
    });

    testWidgets('PairingRenotifyButton dispatches PairingRenotifyRequestedAction on tap', (
      WidgetTester tester,
    ) async {
      final actions = <dynamic>[];
      final store = Store<AppState>(
        (AppState state, dynamic action) {
          actions.add(action);
          return state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingRenotifyButton()),
          ),
        ),
      );

      await tester.tap(find.byType(ElevatedButton));

      expect(actions, contains(isA<PairingRenotifyRequestedAction>()));
    });

    testWidgets('PairingRenotifyButton does not dispatch action during cooldown', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final availableIn5Seconds = now.add(const Duration(seconds: 5));
      final actions = <dynamic>[];
      final store = Store<AppState>(
        (AppState state, dynamic action) {
          actions.add(action);
          return state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: availableIn5Seconds,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingRenotifyButton()),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);

      await tester.tap(find.byType(ElevatedButton), warnIfMissed: false);

      expect(actions.whereType<PairingRenotifyRequestedAction>(), isEmpty);
    });

    testWidgets(
      'PairingRenotifyButton updates label when state transitions from cooldown to available',
      (WidgetTester tester) async {
        final now = DateTime.now();
        final availableIn5Seconds = now.add(const Duration(seconds: 5));

        final store = Store<AppState>(
          (AppState state, dynamic action) {
            if (action is _TransitionCooldownAction) {
              return AppState(
                connection: ConnectionState.initial(),
                pairing: const PairingState(
                  phase: PairingPhase.awaitingCode,
                  bridgeVersion: null,
                  error: null,
                  codeExpiresAt: null,
                  renotifyAvailableAt: null,
                ),
              );
            }
            return state;
          },
          initialState: AppState(
            connection: ConnectionState.initial(),
            pairing: PairingState(
              phase: PairingPhase.awaitingCode,
              bridgeVersion: null,
              error: null,
              codeExpiresAt: null,
              renotifyAvailableAt: availableIn5Seconds,
            ),
          ),
        );

        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const Scaffold(body: PairingRenotifyButton()),
            ),
          ),
        );

        var text = tester.widget<Text>(find.byType(Text));
        expect(text.data, matches(r'Send Code Again \(\d+s\)'));

        store.dispatch(_TransitionCooldownAction());
        await tester.pump();

        text = tester.widget<Text>(find.byType(Text));
        expect(text.data, 'Send Code Again');
      },
    );

    testWidgets('PairingRenotifyButton becomes enabled once the cooldown elapses with no Redux dispatch at all', (
      WidgetTester tester,
    ) async {
      final store = Store<AppState>(
        (AppState state, dynamic action) {
          if (action is _SetRenotifyAvailableAtAction) {
            return AppState(
              connection: state.connection,
              pairing: state.pairing.copyWith(
                renotifyAvailableAt: Some(action.availableAt),
              ),
            );
          }
          return state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          // Set once pumpWidget below has already settled, so real test-framework startup
          // overhead can't eat into this margin the way capturing `now` beforehand would.
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: null,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingRenotifyButton()),
          ),
        ),
      );

      store.dispatch(
        _SetRenotifyAvailableAtAction(
          DateTime.now().add(const Duration(seconds: 2)),
        ),
      );
      await tester.pump();
      expect(
        tester.widget<ElevatedButton>(find.byType(ElevatedButton)).onPressed,
        isNull,
      );

      // Two clocks are in play here: `renotifyCooldownSecondsSelector` reads real
      // DateTime.now(), which only runAsync's real wait can advance; the widget's own
      // Timer.periodic runs on flutter_test's simulated clock, which only tester.pump(duration)
      // can advance. Neither alone triggers a rebuild that observes the elapsed cooldown -- both
      // are needed, with no further Redux dispatch involved at all.
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(seconds: 3)),
      );
      await tester.pump(const Duration(seconds: 2));

      expect(
        tester.widget<ElevatedButton>(find.byType(ElevatedButton)).onPressed,
        isNotNull,
      );
    });
  });
}

class _SetRenotifyAvailableAtAction {
  _SetRenotifyAvailableAtAction(this.availableAt);
  final DateTime availableAt;
}

class _TransitionCooldownAction {
  _TransitionCooldownAction();
}
