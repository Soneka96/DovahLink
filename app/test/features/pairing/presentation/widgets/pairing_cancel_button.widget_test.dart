import 'package:flutter/material.dart' hide ConnectionState;
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_cancel_button.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises [PairingCancelButton] dispatch behavior and enabled/disabled states.
void main() {
  group('PairingCancelButton', () {
    testWidgets('PairingCancelButton displays enabled button during awaiting-code phase', (
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
            child: const Scaffold(body: PairingCancelButton()),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Cancel');
    });

    testWidgets('PairingCancelButton displays disabled button in disconnected phase', (
      WidgetTester tester,
    ) async {
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.disconnected,
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
            child: const Scaffold(body: PairingCancelButton()),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('PairingCancelButton displays disabled button in failed phase', (
      WidgetTester tester,
    ) async {
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.failed,
            bridgeVersion: null,
            error: 'Challenge cancelled',
            codeExpiresAt: null,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingCancelButton()),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('PairingCancelButton displays disabled button in succeeded phase', (
      WidgetTester tester,
    ) async {
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.trusted,
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
            child: const Scaffold(body: PairingCancelButton()),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('PairingCancelButton dispatches PairingCancelRequestedAction on tap', (
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
            child: const Scaffold(body: PairingCancelButton()),
          ),
        ),
      );

      await tester.tap(find.byType(ElevatedButton));

      expect(actions, contains(isA<PairingCancelRequestedAction>()));
    });

    testWidgets('PairingCancelButton does not dispatch action when disabled', (
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
            phase: PairingPhase.disconnected,
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
            child: const Scaffold(body: PairingCancelButton()),
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);

      await tester.tap(find.byType(ElevatedButton), warnIfMissed: false);

      expect(actions.whereType<PairingCancelRequestedAction>(), isEmpty);
    });

    testWidgets('PairingCancelButton uses custom label when provided', (WidgetTester tester) async {
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
            child: const Scaffold(
              body: PairingCancelButton(label: 'Exit Pairing'),
            ),
          ),
        ),
      );

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.data, 'Exit Pairing');
    });

    testWidgets('PairingCancelButton updates when phase changes', (WidgetTester tester) async {
      final store = Store<AppState>(
        (AppState state, dynamic action) {
          if (action is _TransitionPhaseAction) {
            return AppState(
              connection: ConnectionState.initial(),
              pairing: const PairingState(
                phase: PairingPhase.disconnected,
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
            child: const Scaffold(body: PairingCancelButton()),
          ),
        ),
      );

      var button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNotNull);

      store.dispatch(_TransitionPhaseAction(PairingPhase.disconnected));
      await tester.pump();

      button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('PairingCancelButton re-enables when phase returns to awaiting-code', (
      WidgetTester tester,
    ) async {
      final store = Store<AppState>(
        (AppState state, dynamic action) {
          if (action is _TransitionPhaseAction) {
            if (action.toPhase == PairingPhase.awaitingCode) {
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
            } else {
              return AppState(
                connection: ConnectionState.initial(),
                pairing: PairingState(
                  phase: action.toPhase,
                  bridgeVersion: null,
                  error: null,
                  codeExpiresAt: null,
                  renotifyAvailableAt: null,
                ),
              );
            }
          }
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
            child: const Scaffold(body: PairingCancelButton()),
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

    testWidgets('PairingCancelButton applies custom style when provided', (
      WidgetTester tester,
    ) async {
      const customStyle = ButtonStyle(
        backgroundColor: WidgetStatePropertyAll<Color>(Colors.red),
      );
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
            child: const Scaffold(
              body: PairingCancelButton(style: customStyle),
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
