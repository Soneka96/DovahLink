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
    testWidgets('PairingCountdown renders nothing when countdown is null', (
      WidgetTester tester,
    ) async {
      final store = Store<AppState>(
        (AppState state, dynamic action) => state,
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
            hostVersion: null,
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

    testWidgets(
      'PairingCountdown displays formatted countdown when seconds remain',
      (WidgetTester tester) async {
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
              hostVersion: null,
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
      },
    );

    testWidgets('PairingCountdown displays zero when countdown has expired', (
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
            hostVersion: null,
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

    testWidgets('PairingCountdown applies custom text style when provided', (
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
            hostVersion: null,
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

    testWidgets('PairingCountdown uses custom format function when provided', (
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
            hostVersion: null,
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

    testWidgets('PairingCountdown updates countdown when store state changes', (
      WidgetTester tester,
    ) async {
      final now = DateTime.now();
      final expiresIn60Seconds = now.add(
        const Duration(seconds: 60, milliseconds: 500),
      );
      final expiresIn30Seconds = now.add(
        const Duration(seconds: 30, milliseconds: 500),
      );
      final List<int> formattedSeconds = [];

      final store = Store<AppState>(
        (AppState state, dynamic action) {
          if (action is _UpdateCountdownAction) {
            return AppState(
              connection: ConnectionState.initial(),
              pairing: PairingState(
                phase: PairingPhase.awaitingCode,
                hostVersion: null,
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
            hostVersion: null,
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
            child: Scaffold(
              body: PairingCountdown(
                formatSeconds: (int seconds) {
                  formattedSeconds.add(seconds);
                  return '${seconds}s';
                },
              ),
            ),
          ),
        ),
      );

      var textWidget = tester.widget<Text>(find.byType(Text));
      expect(textWidget.data, '60s');
      expect(formattedSeconds, [60]);

      store.dispatch(_UpdateCountdownAction(expiresIn60Seconds));
      await tester.pump();

      expect(formattedSeconds, [60]);

      store.dispatch(_UpdateCountdownAction(expiresIn30Seconds));
      await tester.pump();

      textWidget = tester.widget<Text>(find.byType(Text));
      expect(textWidget.data, '30s');
      expect(formattedSeconds, [60, 30]);
    });

    testWidgets('PairingCountdown maintains timer during widget rebuild', (
      WidgetTester tester,
    ) async {
      final store = Store<AppState>(
        (AppState state, dynamic action) {
          if (action is _UpdateCountdownAction) {
            return AppState(
              connection: state.connection,
              pairing: PairingState(
                phase: state.pairing.phase,
                hostVersion: state.pairing.hostVersion,
                error: state.pairing.error,
                codeExpiresAt: action.newExpiry,
                renotifyAvailableAt: state.pairing.renotifyAvailableAt,
              ),
            );
          }
          return state;
        },
        initialState: AppState(
          connection: ConnectionState.initial(),
          pairing: const PairingState(
            phase: PairingPhase.awaitingCode,
            hostVersion: null,
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
            child: Scaffold(
              body: PairingCountdown(
                formatSeconds: (int seconds) => '$seconds',
              ),
            ),
          ),
        ),
      );

      store.dispatch(
        _UpdateCountdownAction(DateTime.now().add(const Duration(seconds: 10))),
      );
      await tester.pump();

      final int initialSeconds = int.parse(
        tester.widget<Text>(find.byType(Text)).data!,
      );
      expect(initialSeconds, greaterThan(0));

      // The selector reads real time; pump advances the widget timer.
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 1100)),
      );
      await tester.pump(const Duration(seconds: 1));

      final int elapsedSeconds = int.parse(
        tester.widget<Text>(find.byType(Text)).data!,
      );
      expect(elapsedSeconds, lessThan(initialSeconds));
      expect(elapsedSeconds, greaterThan(0));
    });
  });
}

class _UpdateCountdownAction {
  _UpdateCountdownAction(this.newExpiry);
  final DateTime newExpiry;
}
