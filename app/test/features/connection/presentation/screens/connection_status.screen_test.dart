import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/screens/connection_status.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Exercises connection-status rendering and accessibility behavior.
void main() {
  setUp(initDependencies);

  group('ConnectionStatusScreen contains widgets', () {
    testWidgets('contains the disconnected status with correct parameters', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: const CreateStore()(),
            child: const ConnectionStatusScreen(),
          ),
        ),
      );

      expect(find.byKey(const Key('connection-status')), findsOneWidget);
      expect(find.text('Disconnected'), findsOneWidget);
      expect(find.byKey(const Key('connection-error')), findsNothing);
      expect(find.textContaining('Session:'), findsNothing);
    });

    testWidgets('contains the connected session with correct parameters', (
      WidgetTester tester,
    ) async {
      final Store<AppState> store = const CreateStore()();
      store.dispatch(
        const ConnectionEstablishedAction(
          ConnectionSessionEntity(sessionId: 'session-1'),
        ),
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const ConnectionStatusScreen(),
          ),
        ),
      );

      expect(find.text('Connected'), findsOneWidget);
      expect(find.text('Session: session-1'), findsOneWidget);
      expect(find.byKey(const Key('connection-error')), findsNothing);
    });
  });

  group('ConnectionStatusScreen displays connection states', () {
    testWidgets('displays a connection error when it exists', (
      WidgetTester tester,
    ) async {
      final Store<AppState> store = const CreateStore()();
      store.dispatch(const ConnectionUnavailableAction('Bridge unavailable'));

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const ConnectionStatusScreen(),
          ),
        ),
      );

      expect(find.text('Unavailable'), findsOneWidget);
      expect(find.byKey(const Key('connection-error')), findsOneWidget);
      expect(find.text('Bridge unavailable'), findsOneWidget);
    });
  });

  group(
    'ConnectionStatusScreen meets accessibility recommended guidelines',
    () {
      testWidgets('contains a semantic connection status label', (
        WidgetTester tester,
      ) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(
            MaterialApp(
              home: StoreProvider<AppState>(
                store: const CreateStore()(),
                child: const ConnectionStatusScreen(),
              ),
            ),
          );

          expect(
            tester.getSemantics(find.byKey(const Key('connection-status'))),
            matchesSemantics(label: 'Disconnected'),
          );
        } finally {
          handle.dispose();
        }
      });
    },
  );
}
