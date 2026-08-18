import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/renotify_button_widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises [RenotifyButtonWidget] dispatch behavior and enabled/disabled states.
void main() {
  group('RenotifyButtonWidget', () {
    testWidgets('displays enabled button when renotify is available',
        (WidgetTester tester) async {
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: RenotifyButtonWidget(),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Send Code Again');
    });

    testWidgets('displays disabled button during cooldown', (WidgetTester tester) async {
      final now = DateTime.now();
      final availableIn5Seconds = now.add(const Duration(seconds: 5));
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            renotifyAvailableAt: availableIn5Seconds,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: RenotifyButtonWidget(),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('shows cooldown seconds in default format', (WidgetTester tester) async {
      final now = DateTime.now();
      final availableIn3Seconds = now.add(const Duration(seconds: 3));
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            renotifyAvailableAt: availableIn3Seconds,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: RenotifyButtonWidget(),
            ),
          ),
        ),
      );

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, matches(r'Send Code Again \(\d+s\)'));
    });

    testWidgets('treats zero cooldown seconds as available', (WidgetTester tester) async {
      final now = DateTime.now();
      final alreadyElapsed = now.subtract(const Duration(seconds: 1));
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            renotifyAvailableAt: alreadyElapsed,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: RenotifyButtonWidget(),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Send Code Again');
    });

    testWidgets('uses custom cooldown label when provided', (WidgetTester tester) async {
      final now = DateTime.now();
      final availableIn5Seconds = now.add(const Duration(seconds: 5));
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            renotifyAvailableAt: availableIn5Seconds,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: RenotifyButtonWidget(
                cooldownLabel: 'Please wait...',
              ),
            ),
          ),
        ),
      );

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Please wait...');
    });

    testWidgets('dispatches PairingRenotifyRequestedAction on tap',
        (WidgetTester tester) async {
      final actions = <dynamic>[];
      final store = Store<AppState>(
        (dynamic action) {
          actions.add(action);
          return store.state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: RenotifyButtonWidget(),
            ),
          ),
        ),
      );

      await tester.tap(find.byType(ElevatedButton));

      expect(actions, contains(isA<PairingRenotifyRequestedAction>()));
    });

    testWidgets('does not dispatch action during cooldown', (WidgetTester tester) async {
      final now = DateTime.now();
      final availableIn5Seconds = now.add(const Duration(seconds: 5));
      final actions = <dynamic>[];
      final store = Store<AppState>(
        (dynamic action) {
          actions.add(action);
          return store.state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            renotifyAvailableAt: availableIn5Seconds,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: RenotifyButtonWidget(),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);

      await tester.tap(find.byType(ElevatedButton), warnIfMissed: false);

      expect(
        actions.whereType<PairingRenotifyRequestedAction>(),
        isEmpty,
      );
    });

    testWidgets('updates label when state transitions from cooldown to available',
        (WidgetTester tester) async {
      final now = DateTime.now();
      final availableIn5Seconds = now.add(const Duration(seconds: 5));

      final store = Store<AppState>(
        (dynamic action) {
          if (action is _TransitionCooldownAction) {
            return AppState(
              connection: ConnectionState.initial(),
              pairing: const PairingState(
                phase: PairingPhase.awaitingCode,
                renotifyAvailableAt: null,
              ),
            );
          }
          return store.state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            renotifyAvailableAt: availableIn5Seconds,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: RenotifyButtonWidget(),
            ),
          ),
        ),
      );

      var text = tester.widget<Text>(find.byType(Text));
      expect(text.data, matches(r'Send Code Again \(\d+s\)'));

      store.dispatch(_TransitionCooldownAction());
      await tester.pump();

      text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Send Code Again');
    });
  });
}

class _TransitionCooldownAction {
  _TransitionCooldownAction();
}
