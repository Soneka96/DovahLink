import 'package:flutter/material.dart' hide ConnectionState;
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_countdown.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises [PairingCountdown] countdown display and periodic updates.
void main() {
  group('PairingCountdown', () {
    testWidgets('renders nothing when countdown is null', (
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
            child: const Scaffold(body: PairingCountdown()),
          ),
        ),
      );

      expect(find.byType(Text), findsNothing);
    });

    testWidgets('displays formatted countdown when seconds remain', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      // A small margin absorbs the wall-clock time pumpWidget takes, so the
      // truncating-to-whole-seconds selector doesn't flake across a second boundary.
      final expiresIn125Seconds = now.add(
        const Duration(seconds: 125, milliseconds: 500),
      );
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: expiresIn125Seconds,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingCountdown()),
          ),
        ),
      );

      final textFinder = find.byType(Text);
      expect(textFinder, findsOneWidget);
      final Text textWidget = tester.widget<Text>(textFinder);
      expect(textWidget.data, '2:05');
    });

    testWidgets('displays zero when countdown has expired', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final expiredInPast = now.subtract(const Duration(seconds: 10));
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: expiredInPast,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingCountdown()),
          ),
        ),
      );

      final textFinder = find.byType(Text);
      expect(textFinder, findsOneWidget);
      final Text textWidget = tester.widget<Text>(textFinder);
      expect(textWidget.data, '0:00');
    });

    testWidgets('applies custom text style when provided', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final expiresIn10Seconds = now.add(const Duration(seconds: 10));
      const customStyle = TextStyle(fontSize: 32, fontWeight: FontWeight.bold);
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: expiresIn10Seconds,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(
              body: PairingCountdown(textStyle: customStyle),
            ),
          ),
        ),
      );

      final textFinder = find.byType(Text);
      expect(textFinder, findsOneWidget);
      final Text textWidget = tester.widget<Text>(textFinder);
      expect(textWidget.style, customStyle);
    });

    testWidgets('uses custom format function when provided', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final expiresIn90Seconds = now.add(const Duration(seconds: 90));
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: expiresIn90Seconds,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: Scaffold(
              body: PairingCountdown(
                formatSeconds: (secs) => '${secs}s remaining',
              ),
            ),
          ),
        ),
      );

      final textFinder = find.byType(Text);
      expect(textFinder, findsOneWidget);
      final Text textWidget = tester.widget<Text>(textFinder);
      expect(textWidget.data, contains('s remaining'));
    });

    testWidgets('updates countdown when store state changes', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final expiresIn60Seconds = now.add(
        const Duration(seconds: 60, milliseconds: 500),
      );
      final expiresIn30Seconds = now.add(
        const Duration(seconds: 30, milliseconds: 500),
      );

      final store = Store<AppState>(
        (AppState state, dynamic action) {
          if (action is _UpdateCountdownAction) {
            return AppState(
              connection: ConnectionState.initial(),
              pairing: PairingState(
                phase: PairingPhase.awaitingCode,
                bridgeVersion: null,
                error: null,
                codeExpiresAt: action.newExpiry,
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
            codeExpiresAt: expiresIn60Seconds,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingCountdown()),
          ),
        ),
      );

      var textWidget = tester.widget<Text>(find.byType(Text));
      expect(textWidget.data, '1:00');

      store.dispatch(_UpdateCountdownAction(expiresIn30Seconds));
      await tester.pump();

      textWidget = tester.widget<Text>(find.byType(Text));
      expect(textWidget.data, '0:30');
    });

    testWidgets('maintains timer during widget rebuild', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final expiresIn60Seconds = now.add(
        const Duration(seconds: 60, milliseconds: 500),
      );
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: PairingState(
            phase: PairingPhase.awaitingCode,
            bridgeVersion: null,
            error: null,
            codeExpiresAt: expiresIn60Seconds,
            renotifyAvailableAt: null,
          ),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const Scaffold(body: PairingCountdown()),
          ),
        ),
      );

      final textBefore = (tester.widget<Text>(find.byType(Text))).data;
      expect(textBefore, '1:00');

      await tester.pump();

      final textAfter = (tester.widget<Text>(find.byType(Text))).data;
      expect(textAfter, isNotNull);
    });
  });
}

class _UpdateCountdownAction {
  _UpdateCountdownAction(this.newExpiry);
  final DateTime newExpiry;
}
