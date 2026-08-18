import 'package:flutter/material.dart' hide ConnectionState;
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/screens/pairing.screen.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_screen.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_back_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_cancel_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_form.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_countdown.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_loading.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_renotify_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_request_code_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_retry_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_trusted.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Mocktail double for [PairingScreenViewModel], the DI-resolved dependency
/// [PairingScreen] converts to -- mocked the same as any other, never
/// hand-built or derived from a real store.
class MockPairingScreenViewModel extends Mock
    implements PairingScreenViewModel {}

/// Mocktail double for [Store], standing in for the `Store<AppState>` passed
/// to [StoreProvider]. `onInit`/`onDispose` read and dispatch on it directly
/// rather than through the ViewModel, so it is mocked too, exactly like this
/// project's middleware tests: `dispatch` records into an action log,
/// `state` returns whatever a test needs directly.
class MockStore extends Mock implements Store<AppState> {}

void main() {
  late MockPairingScreenViewModel mockViewModel;
  late MockStore store;
  late List<Object?> dispatchedActions;

  setUp(() async {
    await sl.reset();

    mockViewModel = MockPairingScreenViewModel();
    when(() => mockViewModel.phase).thenReturn(PairingPhase.none);
    when(() => mockViewModel.statusLabel).thenReturn('Unknown');
    when(() => mockViewModel.bridgeVersion).thenReturn(null);
    when(() => mockViewModel.error).thenReturn(null);
    when(() => mockViewModel.onStart).thenReturn(() {});
    when(() => mockViewModel.onRequestCode).thenReturn(() {});
    when(
      () => mockViewModel.onSubmitCode,
    ).thenReturn((String _, String? _) {});
    when(() => mockViewModel.onBack).thenReturn(() {});
    sl.registerFactoryParam<PairingScreenViewModel, Store<AppState>, void>((
      Store<AppState> store,
      void _,
    ) {
      return mockViewModel;
    });

    dispatchedActions = [];
    store = MockStore();
    when(() => store.dispatch(any())).thenAnswer(
      (Invocation invocation) =>
          dispatchedActions.add(invocation.positionalArguments[0]),
    );
    when(() => store.state).thenReturn(
      AppState(connection: ConnectionState.initial(), pairing: PairingState.initial()),
    );
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
  });

  tearDown(() async {
    await sl.reset();
    reset(mockViewModel);
    reset(store);
  });

  Widget buildWidget({TextScaler textScaler = TextScaler.noScaling}) =>
      StoreProvider<AppState>(
        store: store,
        child: MaterialApp(
          builder: (BuildContext context, Widget? child) => MediaQuery(
            data: MediaQuery.of(context).copyWith(textScaler: textScaler),
            child: child ?? const SizedBox.shrink(),
          ),
          home: const PairingScreen(),
        ),
      );

  group('PairingScreen contains widgets', () {
    testWidgets('shows a loading indicator when connecting', (
      WidgetTester tester,
    ) async {
      when(() => mockViewModel.phase).thenReturn(PairingPhase.connecting);
      when(() => mockViewModel.statusLabel).thenReturn('Connecting');

      await tester.pumpWidget(buildWidget());

      expect(find.text('Connecting'), findsOneWidget);
      expect(find.byType(PairingLoadingIndicator), findsOneWidget);
    });

    testWidgets('contains the back button on mount', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(find.byType(PairingBackButton), findsOneWidget);
    });

    testWidgets(
      'contains the back button once trusted, not just while connecting',
      (WidgetTester tester) async {
        // The back button sits in the AppBar, outside the phase switch that
        // picks the body content, so this and the mount-time check together
        // cover it: unconditional placement means no phase can hide it.
        when(() => mockViewModel.phase).thenReturn(PairingPhase.trusted);
        when(() => mockViewModel.statusLabel).thenReturn('Paired');

        await tester.pumpWidget(buildWidget());

        expect(find.byType(PairingBackButton), findsOneWidget);
      },
    );

    testWidgets('contains the request-code button when unpaired', (
      WidgetTester tester,
    ) async {
      when(() => mockViewModel.phase).thenReturn(PairingPhase.unpaired);
      when(() => mockViewModel.statusLabel).thenReturn('Not paired');

      await tester.pumpWidget(buildWidget());

      expect(find.text('Not paired'), findsOneWidget);
      expect(find.byType(PairingRequestCodeButton), findsOneWidget);
    });

    testWidgets('contains the code entry form when awaiting a code', (
      WidgetTester tester,
    ) async {
      when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
      when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');

      await tester.pumpWidget(buildWidget());

      expect(find.text('Awaiting code'), findsOneWidget);
      expect(find.byType(PairingCodeForm), findsOneWidget);
    });

    testWidgets(
      'contains countdown widget when awaiting a code',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');

        await tester.pumpWidget(buildWidget());

        expect(find.byType(PairingCountdown), findsOneWidget);
      },
    );

    testWidgets(
      'contains renotify and cancel buttons when awaiting a code',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');

        await tester.pumpWidget(buildWidget());

        expect(find.byType(PairingRenotifyButton), findsOneWidget);
        expect(find.byType(PairingCancelButton), findsOneWidget);
      },
    );

    testWidgets('contains the trusted state once paired', (
      WidgetTester tester,
    ) async {
      when(() => mockViewModel.phase).thenReturn(PairingPhase.trusted);
      when(() => mockViewModel.statusLabel).thenReturn('Paired');

      await tester.pumpWidget(buildWidget());

      expect(find.byType(PairingTrustedIndicator), findsOneWidget);
    });

    testWidgets(
      'shows a neutral waiting state with no error when the bridge is disconnected',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.disconnected);
        when(
          () => mockViewModel.statusLabel,
        ).thenReturn('Waiting for bridge');

        await tester.pumpWidget(buildWidget());

        expect(find.text('Waiting for bridge'), findsOneWidget);
        expect(find.byType(PairingLoadingIndicator), findsOneWidget);
        expect(find.byKey(const Key('pairing-error')), findsNothing);
        expect(find.byType(PairingRetryButton), findsNothing);
      },
    );

    testWidgets('displays a pairing error and a retry button when failed', (
      WidgetTester tester,
    ) async {
      when(() => mockViewModel.phase).thenReturn(PairingPhase.failed);
      when(() => mockViewModel.statusLabel).thenReturn('Failed');
      when(() => mockViewModel.error).thenReturn("That code isn't correct.");

      await tester.pumpWidget(buildWidget());

      expect(find.text('Failed'), findsOneWidget);
      expect(find.byKey(const Key('pairing-error')), findsOneWidget);
      expect(find.text("That code isn't correct."), findsOneWidget);
      expect(find.byType(PairingRetryButton), findsOneWidget);
    });
  });

  group("PairingScreen's elements behavior", () {
    testWidgets('tapping the back button calls onBack', (
      WidgetTester tester,
    ) async {
      bool called = false;
      when(() => mockViewModel.onBack).thenReturn(() => called = true);

      await tester.pumpWidget(buildWidget());
      await tester.tap(find.byKey(const Key('pairing-back-button')));
      await tester.pump();

      expect(called, isA<bool>());
      expect(called, isTrue);
    });

    testWidgets('tapping the request-code button calls onRequestCode', (
      WidgetTester tester,
    ) async {
      bool called = false;
      when(() => mockViewModel.phase).thenReturn(PairingPhase.unpaired);
      when(() => mockViewModel.statusLabel).thenReturn('Not paired');
      when(() => mockViewModel.onRequestCode).thenReturn(() => called = true);

      await tester.pumpWidget(buildWidget());
      await tester.tap(find.byKey(const Key('pairing-request-code-button')));
      await tester.pump();

      expect(called, isA<bool>());
      expect(called, isTrue);
    });

    testWidgets(
      'submitting the code form calls onSubmitCode with the entered values',
      (WidgetTester tester) async {
        String? capturedCode;
        String? capturedDisplayName;
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');
        when(() => mockViewModel.onSubmitCode).thenReturn((
          String code,
          String? displayName,
        ) {
          capturedCode = code;
          capturedDisplayName = displayName;
        });

        await tester.pumpWidget(buildWidget());
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

        expect(capturedCode, isA<String>());
        expect(capturedCode, '123456');
        expect(capturedDisplayName, isA<String>());
        expect(capturedDisplayName, 'Desktop');
      },
    );

    testWidgets(
      'submitting the code form with no display name calls onSubmitCode with a null displayName',
      (WidgetTester tester) async {
        String? capturedCode;
        String? capturedDisplayName = 'not null yet';
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');
        when(() => mockViewModel.onSubmitCode).thenReturn((
          String code,
          String? displayName,
        ) {
          capturedCode = code;
          capturedDisplayName = displayName;
        });

        await tester.pumpWidget(buildWidget());
        await tester.enterText(
          find.byKey(const Key('pairing-code-field')),
          '123456',
        );
        await tester.tap(find.byKey(const Key('pairing-confirm-button')));
        await tester.pump();

        expect(capturedCode, isA<String>());
        expect(capturedCode, '123456');
        expect(capturedDisplayName, isNull);
      },
    );

    testWidgets('tapping the retry button calls onStart', (
      WidgetTester tester,
    ) async {
      bool called = false;
      when(() => mockViewModel.phase).thenReturn(PairingPhase.failed);
      when(() => mockViewModel.statusLabel).thenReturn('Failed');
      when(() => mockViewModel.error).thenReturn('unavailable');
      when(() => mockViewModel.onStart).thenReturn(() => called = true);

      await tester.pumpWidget(buildWidget());
      await tester.tap(find.byKey(const Key('pairing-retry-button')));
      await tester.pump();

      expect(called, isA<bool>());
      expect(called, isTrue);
    });
  });

  group("PairingScreen's StoreConnector dispatches actions on the Store", () {
    testWidgets('dispatches PairingStartedAction on mount', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(dispatchedActions, contains(const PairingStartedAction()));
    });

    testWidgets('dispatches PairingDisposedAction when the screen unmounts', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());
      await tester.pumpWidget(const SizedBox.shrink());

      expect(
        dispatchedActions,
        contains(const PairingDisposedAction(wasTrusted: false)),
      );
      expect(tester.takeException(), isNull);
    });

    testWidgets(
      'dispatches PairingDisposedAction with wasTrusted when the Store reports trusted',
      (WidgetTester tester) async {
        when(() => store.state).thenReturn(
          AppState(
            connection: ConnectionState.initial(),
            pairing: const PairingState(
              phase: PairingPhase.trusted,
              bridgeVersion: '1.2.3',
              error: null,
              codeExpiresAt: null,
              renotifyAvailableAt: null,
            ),
          ),
        );

        await tester.pumpWidget(buildWidget());
        await tester.pumpWidget(const SizedBox.shrink());

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
      when(() => mockViewModel.phase).thenReturn(PairingPhase.connecting);
      when(() => mockViewModel.statusLabel).thenReturn('Connecting');
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(buildWidget());

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
      when(() => mockViewModel.phase).thenReturn(PairingPhase.disconnected);
      when(() => mockViewModel.statusLabel).thenReturn('Waiting for bridge');
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(buildWidget());

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
      when(() => mockViewModel.phase).thenReturn(PairingPhase.failed);
      when(() => mockViewModel.statusLabel).thenReturn('Failed');
      when(() => mockViewModel.error).thenReturn("That code isn't correct.");
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(buildWidget());

        await expectLater(tester, meetsGuideline(textContrastGuideline));
      } finally {
        handle.dispose();
      }
    });

    testWidgets(
      'lays out the code form without overflow at a large text scale',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');

        await tester.pumpWidget(
          buildWidget(textScaler: const TextScaler.linear(2.0)),
        );

        expect(tester.takeException(), isNull);
        expect(find.byKey(const Key('pairing-code-field')), findsOneWidget);
        expect(
          find.byKey(const Key('pairing-confirm-button')),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'lays out countdown and buttons without overflow at large text scale',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');

        await tester.pumpWidget(
          buildWidget(textScaler: const TextScaler.linear(2.0)),
        );

        expect(tester.takeException(), isNull);
        expect(find.byType(PairingCountdown), findsOneWidget);
        expect(find.byType(PairingRenotifyButton), findsOneWidget);
        expect(find.byType(PairingCancelButton), findsOneWidget);
      },
    );

    testWidgets(
      'does not show countdown or buttons outside awaitingCode phase',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.connecting);
        when(() => mockViewModel.statusLabel).thenReturn('Connecting');

        await tester.pumpWidget(buildWidget());

        expect(find.byType(PairingCountdown), findsNothing);
        expect(find.byType(PairingRenotifyButton), findsNothing);
        expect(find.byType(PairingCancelButton), findsNothing);
      },
    );
  });
}
