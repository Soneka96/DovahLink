import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/screens/pairing.screen.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_back_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_form.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_loading.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_request_code_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_retry_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_trusted.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// A middleware that records every dispatched action without altering
/// behavior, so tests can assert exactly what [PairingScreen] dispatched.
Middleware<AppState> _spy(List<Object?> dispatchedActions) {
  return (Store<AppState> store, dynamic action, NextDispatcher next) {
    dispatchedActions.add(action);
    next(action);
  };
}

/// Exercises pairing-screen rendering, dispatch, and accessibility behavior.
///
/// Per-widget rendering/interaction detail (button labels, form field
/// behavior, focus order) lives in each extracted widget's own test file
/// under `presentation/widgets/`; this file only proves the screen selects
/// the correct widget per phase and wires Redux dispatch correctly.
void main() {
  setUp(initDependencies);

  group('PairingScreen contains widgets', () {
    testWidgets('dispatches PairingStartedAction and shows a loading indicator on mount', (
      WidgetTester tester,
    ) async {
      final Store<AppState> store = const CreateStore()();

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const PairingScreen(),
          ),
        ),
      );

      expect(find.text('Connecting'), findsOneWidget);
      expect(find.byType(PairingLoadingIndicator), findsOneWidget);
    });

    testWidgets('contains the back button on mount', (
      WidgetTester tester,
    ) async {
      final Store<AppState> store = const CreateStore()();

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const PairingScreen(),
          ),
        ),
      );

      expect(find.byType(PairingBackButton), findsOneWidget);
    });

    testWidgets('tapping the back button dispatches PairingBackRequestedAction', (
      WidgetTester tester,
    ) async {
      final List<Object?> dispatchedActions = [];
      final Store<AppState> store = const CreateStore()(
        middleware: [_spy(dispatchedActions)],
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const PairingScreen(),
          ),
        ),
      );

      await tester.tap(find.byKey(const Key('pairing-back-button')));
      await tester.pump();

      expect(dispatchedActions, contains(const PairingBackRequestedAction()));
    });

    testWidgets(
      'contains the back button once trusted, not just while connecting',
      (WidgetTester tester) async {
        // The back button sits in the AppBar, outside the phase switch that
        // picks the body content, so this and the mount-time check together
        // cover it: unconditional placement means no phase can hide it.
        final Store<AppState> store = const CreateStore()();
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const PairingScreen(),
            ),
          ),
        );

        store.dispatch(const PairingConfirmedAction());
        await tester.pump();

        expect(find.byType(PairingBackButton), findsOneWidget);
      },
    );

    testWidgets('contains the request-code button when unpaired', (
      WidgetTester tester,
    ) async {
      final Store<AppState> store = const CreateStore()();
      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const PairingScreen(),
          ),
        ),
      );

      store.dispatch(
        const PairingAuthenticatedAction(
          bridgeVersion: '1.2.3',
          trusted: false,
        ),
      );
      await tester.pump();

      expect(find.text('Not paired'), findsOneWidget);
      expect(find.byType(PairingRequestCodeButton), findsOneWidget);
    });

    testWidgets(
      'tapping the request-code button dispatches PairingCodeRequestedAction',
      (WidgetTester tester) async {
        final Store<AppState> store = const CreateStore()();
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const PairingScreen(),
            ),
          ),
        );
        store.dispatch(
          const PairingAuthenticatedAction(
            bridgeVersion: '1.2.3',
            trusted: false,
          ),
        );
        await tester.pump();

        await tester.tap(find.byKey(const Key('pairing-request-code-button')));
        await tester.pump();

        expect(find.text('Requesting code'), findsOneWidget);
      },
    );

    testWidgets('contains the code entry form when awaiting a code', (
      WidgetTester tester,
    ) async {
      final Store<AppState> store = const CreateStore()();
      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const PairingScreen(),
          ),
        ),
      );

      store.dispatch(const PairingCodeAvailableAction());
      await tester.pump();

      expect(find.text('Awaiting code'), findsOneWidget);
      expect(find.byType(PairingCodeForm), findsOneWidget);
    });

    testWidgets(
      'submitting the code form dispatches PairingCodeSubmittedAction with the entered values',
      (WidgetTester tester) async {
        final List<Object?> dispatchedActions = [];
        final Store<AppState> store = const CreateStore()(
          middleware: [_spy(dispatchedActions)],
        );
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const PairingScreen(),
            ),
          ),
        );
        store.dispatch(const PairingCodeAvailableAction());
        await tester.pump();

        await tester.enterText(
          find.byKey(const Key('pairing-code-field')),
          '123456',
        );
        await tester.enterText(
          find.byKey(const Key('pairing-display-name-field')),
          'Desktop',
        );
        await tester.tap(find.byKey(const Key('pairing-confirm-button')));
        await tester.pump();

        expect(
          dispatchedActions,
          contains(
            const PairingCodeSubmittedAction(
              code: '123456',
              displayName: 'Desktop',
            ),
          ),
        );
      },
    );

    testWidgets(
      'submitting the code form with no display name dispatches a null displayName',
      (WidgetTester tester) async {
        final List<Object?> dispatchedActions = [];
        final Store<AppState> store = const CreateStore()(
          middleware: [_spy(dispatchedActions)],
        );
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const PairingScreen(),
            ),
          ),
        );
        store.dispatch(const PairingCodeAvailableAction());
        await tester.pump();

        await tester.enterText(
          find.byKey(const Key('pairing-code-field')),
          '123456',
        );
        await tester.tap(find.byKey(const Key('pairing-confirm-button')));
        await tester.pump();

        expect(
          dispatchedActions,
          contains(const PairingCodeSubmittedAction(code: '123456')),
        );
      },
    );

    testWidgets('contains the trusted state once paired', (
      WidgetTester tester,
    ) async {
      final Store<AppState> store = const CreateStore()();
      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const PairingScreen(),
          ),
        ),
      );

      store.dispatch(const PairingConfirmedAction());
      await tester.pump();

      expect(find.byType(PairingTrustedIndicator), findsOneWidget);
    });

    testWidgets(
      'shows a neutral waiting state with no error when the bridge is disconnected',
      (WidgetTester tester) async {
        final Store<AppState> store = const CreateStore()();
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const PairingScreen(),
            ),
          ),
        );

        store.dispatch(const PairingDisconnectedAction());
        await tester.pump();

        expect(find.text('Waiting for bridge'), findsOneWidget);
        expect(find.byType(PairingLoadingIndicator), findsOneWidget);
        expect(find.byKey(const Key('pairing-error')), findsNothing);
        expect(find.byType(PairingRetryButton), findsNothing);
      },
    );

    testWidgets('displays a pairing error and a retry button when failed', (
      WidgetTester tester,
    ) async {
      final Store<AppState> store = const CreateStore()();
      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const PairingScreen(),
          ),
        ),
      );

      store.dispatch(const PairingFailedAction("That code isn't correct."));
      await tester.pump();

      expect(find.text('Failed'), findsOneWidget);
      expect(find.byKey(const Key('pairing-error')), findsOneWidget);
      expect(find.text("That code isn't correct."), findsOneWidget);
      expect(find.byType(PairingRetryButton), findsOneWidget);
    });

    testWidgets(
      'tapping the retry button dispatches PairingStartedAction',
      (WidgetTester tester) async {
        final Store<AppState> store = const CreateStore()();
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const PairingScreen(),
            ),
          ),
        );
        store.dispatch(const PairingFailedAction('unavailable'));
        await tester.pump();

        await tester.tap(find.byKey(const Key('pairing-retry-button')));
        await tester.pump();

        expect(find.text('Connecting'), findsOneWidget);
        expect(find.byType(PairingLoadingIndicator), findsOneWidget);
      },
    );

    testWidgets('dispatches PairingDisposedAction when the screen unmounts', (
      WidgetTester tester,
    ) async {
      final List<Object?> dispatchedActions = [];
      final Store<AppState> store = const CreateStore()(
        middleware: [_spy(dispatchedActions)],
      );

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const PairingScreen(),
          ),
        ),
      );
      // Mount the code form so its TextEditingControllers exist and this
      // unmount actually exercises their disposal, not just the screen's.
      store.dispatch(const PairingCodeAvailableAction());
      await tester.pump();

      await tester.pumpWidget(
        MaterialApp(
          home: StoreProvider<AppState>(
            store: store,
            child: const SizedBox.shrink(),
          ),
        ),
      );

      expect(
        dispatchedActions,
        contains(const PairingDisposedAction(wasTrusted: false)),
      );
      expect(tester.takeException(), isNull);
    });

    testWidgets(
      'dispatches PairingDisposedAction with wasTrusted when the screen unmounts while trusted',
      (WidgetTester tester) async {
        final List<Object?> dispatchedActions = [];
        final Store<AppState> store = const CreateStore()(
          middleware: [_spy(dispatchedActions)],
        );

        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const PairingScreen(),
            ),
          ),
        );
        // Reach trusted after mount -- onInit's own PairingStartedAction
        // would otherwise overwrite a pre-mount dispatch via the reducer.
        store.dispatch(
          const PairingAuthenticatedAction(
            bridgeVersion: '1.2.3',
            trusted: true,
          ),
        );
        await tester.pump();

        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const SizedBox.shrink(),
            ),
          ),
        );

        expect(
          dispatchedActions,
          contains(const PairingDisposedAction(wasTrusted: true)),
        );
        expect(tester.takeException(), isNull);
      },
    );
  });

  group('PairingScreen meets accessibility recommended guidelines', () {
    testWidgets('contains a semantic pairing status label', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: const CreateStore()(),
              child: const PairingScreen(),
            ),
          ),
        );

        expect(
          tester.getSemantics(find.byKey(const Key('pairing-status'))),
          matchesSemantics(label: 'Connecting'),
        );
      } finally {
        handle.dispose();
      }
    });

    testWidgets('exposes the disconnected status label as semantics', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        final Store<AppState> store = const CreateStore()();
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const PairingScreen(),
            ),
          ),
        );

        store.dispatch(const PairingDisconnectedAction());
        await tester.pump();

        expect(
          tester.getSemantics(find.byKey(const Key('pairing-status'))),
          matchesSemantics(label: 'Waiting for bridge'),
        );
      } finally {
        handle.dispose();
      }
    });

    testWidgets('meets text contrast guidelines when displaying an error', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        final Store<AppState> store = const CreateStore()();
        await tester.pumpWidget(
          MaterialApp(
            home: StoreProvider<AppState>(
              store: store,
              child: const PairingScreen(),
            ),
          ),
        );
        store.dispatch(
          const PairingFailedAction("That code isn't correct."),
        );
        await tester.pump();

        await expectLater(tester, meetsGuideline(textContrastGuideline));
      } finally {
        handle.dispose();
      }
    });

    testWidgets(
      'lays out the code form without overflow at a large text scale',
      (WidgetTester tester) async {
        final Store<AppState> store = const CreateStore()();
        await tester.pumpWidget(
          MediaQuery(
            data: const MediaQueryData(
              textScaler: TextScaler.linear(2.0),
            ),
            child: MaterialApp(
              home: StoreProvider<AppState>(
                store: store,
                child: const PairingScreen(),
              ),
            ),
          ),
        );
        store.dispatch(const PairingCodeAvailableAction());
        await tester.pump();

        expect(tester.takeException(), isNull);
        expect(find.byKey(const Key('pairing-code-field')), findsOneWidget);
        expect(find.byKey(const Key('pairing-confirm-button')), findsOneWidget);
      },
    );
  });
}
