import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/screens/pairing.screen.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
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
      expect(find.byKey(const Key('pairing-loading')), findsOneWidget);
    });

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
      expect(
        find.byKey(const Key('pairing-request-code-button')),
        findsOneWidget,
      );
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
      expect(find.byKey(const Key('pairing-code-field')), findsOneWidget);
      expect(
        find.byKey(const Key('pairing-display-name-field')),
        findsOneWidget,
      );
      expect(find.byKey(const Key('pairing-confirm-button')), findsOneWidget);
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

      expect(find.text('Paired'), findsOneWidget);
      expect(find.byKey(const Key('pairing-trusted')), findsOneWidget);
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
        expect(find.byKey(const Key('pairing-loading')), findsOneWidget);
        expect(find.byKey(const Key('pairing-error')), findsNothing);
        expect(find.byKey(const Key('pairing-retry-button')), findsNothing);
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
      expect(find.byKey(const Key('pairing-retry-button')), findsOneWidget);
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
        expect(find.byKey(const Key('pairing-loading')), findsOneWidget);
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

      expect(dispatchedActions, contains(const PairingDisposedAction()));
      expect(tester.takeException(), isNull);
    });
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

    testWidgets(
      'labels the request-code button and meets its minimum interactive size',
      (WidgetTester tester) async {
        // DovahLink is a Windows desktop app, not a touch device, so this
        // checks labeledTapTargetGuideline (semantic labeling, platform-
        // neutral) and Material's own kMinInteractiveDimension directly,
        // rather than the mobile-specific androidTapTargetGuideline/
        // iOSTapTargetGuideline -- the app theme's default
        // VisualDensity.adaptivePlatformDensity shrinks targets below the
        // 48dp mobile touch guideline on desktop by design, which is not an
        // accessibility defect here.
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
            const PairingAuthenticatedAction(
              bridgeVersion: '1.2.3',
              trusted: false,
            ),
          );
          await tester.pump();

          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
          final Size buttonSize = tester.getSize(
            find.byKey(const Key('pairing-request-code-button')),
          );
          expect(buttonSize.height, greaterThanOrEqualTo(kMinInteractiveDimension));
        } finally {
          handle.dispose();
        }
      },
    );

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

    testWidgets(
      'traverses focus from the code field to the display-name field in order',
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
        store.dispatch(const PairingCodeAvailableAction());
        await tester.pump();

        final Finder codeField = find.byKey(const Key('pairing-code-field'));
        final Finder displayNameField = find.byKey(
          const Key('pairing-display-name-field'),
        );
        // TextField wraps EditableText around the FocusNode it was given;
        // reading it back from that descendant is the reliable way to
        // identify one specific field's own focus state in a widget test.
        final FocusNode codeFocusNode = tester
            .widget<EditableText>(
              find.descendant(of: codeField, matching: find.byType(EditableText)),
            )
            .focusNode;
        final FocusNode displayNameFocusNode = tester
            .widget<EditableText>(
              find.descendant(
                of: displayNameField,
                matching: find.byType(EditableText),
              ),
            )
            .focusNode;

        await tester.tap(codeField);
        await tester.pump();
        expect(codeFocusNode.hasFocus, isTrue);
        expect(displayNameFocusNode.hasFocus, isFalse);

        codeFocusNode.nextFocus();
        await tester.pump();

        expect(codeFocusNode.hasFocus, isFalse);
        expect(displayNameFocusNode.hasFocus, isTrue);
      },
    );
  });
}
