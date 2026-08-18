import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/cancel_button_widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises [CancelButtonWidget] dispatch behavior and enabled/disabled states.
void main() {
  group('CancelButtonWidget', () {
    testWidgets('displays enabled button during awaiting-code phase',
        (WidgetTester tester) async {
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Cancel');
    });

    testWidgets('displays disabled button in disconnected phase',
        (WidgetTester tester) async {
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.disconnected,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('displays disabled button in failed phase', (WidgetTester tester) async {
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.failed,
            error: 'Challenge cancelled',
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('displays disabled button in succeeded phase', (WidgetTester tester) async {
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.succeeded,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('dispatches PairingCancelRequestedAction on tap',
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
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(),
            ),
          ),
        ),
      );

      await tester.tap(find.byType(ElevatedButton));

      expect(actions, contains(isA<PairingCancelRequestedAction>()));
    });

    testWidgets('does not dispatch action when disabled', (WidgetTester tester) async {
      final actions = <dynamic>[];
      final store = Store<AppState>(
        (dynamic action) {
          actions.add(action);
          return store.state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.disconnected,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);

      await tester.tap(find.byType(ElevatedButton), warnIfMissed: false);

      expect(
        actions.whereType<PairingCancelRequestedAction>(),
        isEmpty,
      );
    });

    testWidgets('uses custom label when provided', (WidgetTester tester) async {
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(label: 'Exit Pairing'),
            ),
          ),
        ),
      );

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Exit Pairing');
    });

    testWidgets('updates when phase changes', (WidgetTester tester) async {
      final store = Store<AppState>(
        (dynamic action) {
          if (action is _TransitionPhaseAction) {
            return AppState(
              connection: ConnectionState.initial(),
              pairing: const PairingState(
                phase: PairingPhase.disconnected,
              ),
            );
          }
          return store.state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(),
            ),
          ),
        ),
      );

      var button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);

      store.dispatch(_TransitionPhaseAction());
      await tester.pump();

      button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('re-enables when phase returns to awaiting-code', (WidgetTester tester) async {
      final store = Store<AppState>(
        (dynamic action) {
          if (action is _TransitionPhaseAction) {
            if (action.toPhase == PairingPhase.awaitingCode) {
              return AppState(
                connection: ConnectionState.initial(),
                pairing: const PairingState(
                  phase: PairingPhase.awaitingCode,
                ),
              );
            } else {
              return AppState(
                connection: ConnectionState.initial(),
                pairing: PairingState(
                  phase: action.toPhase,
                ),
              );
            }
          }
          return store.state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(),
            ),
          ),
        ),
      );

      var button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);

      store.dispatch(_TransitionPhaseAction(PairingPhase.failed));
      await tester.pump();

      button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);

      store.dispatch(_TransitionPhaseAction(PairingPhase.awaitingCode));
      await tester.pump();

      button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);
    });

    testWidgets('applies custom style when provided', (WidgetTester tester) async {
      const customStyle = ButtonStyle(
        backgroundColor: MaterialStatePropertyAll<Color>(Colors.red),
      );
      final store = Store<AppState>(
        (_) {},
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: CancelButtonWidget(style: customStyle),
            ),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.style, customStyle);
    });
  });
}

class _TransitionPhaseAction {
  _TransitionPhaseAction(this.toPhase);
  final PairingPhase toPhase;
}
