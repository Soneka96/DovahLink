import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/screens/pairing.screen.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_countdown.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_renotify_button.viewmodel.dart';
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
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Mocktail double for [PairingScreenViewModel], the DI-resolved dependency
/// [PairingScreen] converts to -- mocked the same as any other, never
/// hand-built or derived from a real store.
class MockPairingScreenViewModel extends Mock
    implements PairingScreenViewModel {}

/// Mocks the cancellation button presentation contract nested in the screen.
class MockPairingCancelButtonViewModel extends Mock
    implements PairingCancelButtonViewModel {}

/// Mocks the countdown presentation contract nested in the screen.
class MockPairingCountdownViewModel extends Mock
    implements PairingCountdownViewModel {}

/// Mocks the redisplay button presentation contract nested in the screen.
class MockPairingRenotifyButtonViewModel extends Mock
    implements PairingRenotifyButtonViewModel {}

/// Mocktail double for the `Store<AppState>` passed to [StoreProvider].
class MockStore extends Mock implements Store<AppState> {}

void main() {
  late MockPairingScreenViewModel mockViewModel;
  late MockPairingCancelButtonViewModel mockCancelViewModel;
  late MockPairingCountdownViewModel mockCountdownViewModel;
  late MockPairingRenotifyButtonViewModel mockRenotifyViewModel;
  late MockStore store;

  setUp(() async {
    await sl.reset();

    mockViewModel = MockPairingScreenViewModel();
    mockCancelViewModel = MockPairingCancelButtonViewModel();
    mockCountdownViewModel = MockPairingCountdownViewModel();
    mockRenotifyViewModel = MockPairingRenotifyButtonViewModel();
    when(() => mockViewModel.phase).thenReturn(PairingPhase.none);
    when(() => mockViewModel.statusLabel).thenReturn('Unknown');
    when(() => mockViewModel.hostVersion).thenReturn(null);
    when(() => mockViewModel.error).thenReturn(null);
    when(() => mockViewModel.onStart).thenReturn(() {});
    when(() => mockViewModel.onRequestCode).thenReturn(() {});
    when(() => mockViewModel.onSubmitCode).thenReturn((String _, String? _) {});
    when(() => mockViewModel.onBack).thenReturn(() {});
    when(() => mockViewModel.onDispose).thenReturn(() {});
    when(() => mockCancelViewModel.isEnabled).thenReturn(false);
    when(() => mockCancelViewModel.onPressed).thenReturn(null);
    when(() => mockCountdownViewModel.remainingSeconds).thenReturn(null);
    when(() => mockRenotifyViewModel.isAvailable).thenReturn(false);
    when(() => mockRenotifyViewModel.cooldownSeconds).thenReturn(3);
    when(() => mockRenotifyViewModel.onPressed).thenReturn(null);
    when(
      () => mockRenotifyViewModel.displayLabel(
        label: any(named: 'label'),
        cooldownLabel: any(named: 'cooldownLabel'),
      ),
    ).thenReturn('Send Code Again (3s)');
    sl.registerFactoryParam<PairingScreenViewModel, Store<AppState>, void>((
      Store<AppState> store,
      void _,
    ) {
      return mockViewModel;
    });
    sl.registerFactoryParam<
      PairingCancelButtonViewModel,
      Store<AppState>,
      void
    >((Store<AppState> _, void _) => mockCancelViewModel);
    sl.registerFactoryParam<PairingCountdownViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => mockCountdownViewModel,
    );
    sl.registerFactoryParam<
      PairingRenotifyButtonViewModel,
      Store<AppState>,
      void
    >((Store<AppState> _, void _) => mockRenotifyViewModel);

    store = MockStore();
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
  });

  tearDown(() async {
    await sl.reset();
    reset(mockViewModel);
    reset(mockCancelViewModel);
    reset(mockCountdownViewModel);
    reset(mockRenotifyViewModel);
    reset(store);
  });

  Widget buildWidget({TextScaler textScaler = TextScaler.noScaling}) =>
      StoreProvider<AppState>(
        store: store,
        child: MaterialApp(
          theme: dovahThemeDataFor(DovahThemePreset.dovah),
          builder: (BuildContext context, Widget? child) => MediaQuery(
            data: MediaQuery.of(context).copyWith(textScaler: textScaler),
            child: child ?? const SizedBox.shrink(),
          ),
          home: const PairingScreen(),
        ),
      );

  group('PairingScreen contains widgets', () {
    testWidgets('PairingScreen shows a loading indicator when connecting', (
      WidgetTester tester,
    ) async {
      when(() => mockViewModel.phase).thenReturn(PairingPhase.connecting);
      when(() => mockViewModel.statusLabel).thenReturn('Connecting');

      await tester.pumpWidget(buildWidget());

      expect(find.text('Connecting'), findsOneWidget);
      expect(find.byType(PairingLoadingIndicator), findsOneWidget);
    });

    testWidgets('PairingScreen contains the back button on mount', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(find.byType(PairingBackButton), findsOneWidget);
    });

    testWidgets(
      'PairingScreen contains the back button once trusted, not just while connecting',
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

    testWidgets(
      'PairingScreen contains the request-code button when unpaired',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.unpaired);
        when(() => mockViewModel.statusLabel).thenReturn('Not paired');

        await tester.pumpWidget(buildWidget());

        expect(find.text('Not paired'), findsOneWidget);
        expect(find.byType(PairingRequestCodeButton), findsOneWidget);
      },
    );

    testWidgets(
      'PairingScreen contains the code entry form when awaiting a code',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');

        await tester.pumpWidget(buildWidget());

        expect(find.text('Awaiting code'), findsOneWidget);
        expect(find.byType(PairingCodeForm), findsOneWidget);
      },
    );

    testWidgets(
      'PairingScreen contains countdown widget when awaiting a code',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');

        await tester.pumpWidget(buildWidget());

        expect(find.byType(PairingCountdown), findsOneWidget);
      },
    );

    testWidgets(
      'PairingScreen contains renotify and cancel buttons when awaiting a code',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');

        await tester.pumpWidget(buildWidget());

        expect(find.byType(PairingRenotifyButton), findsOneWidget);
        expect(find.byType(PairingCancelButton), findsOneWidget);
      },
    );

    testWidgets('PairingScreen contains the trusted state once paired', (
      WidgetTester tester,
    ) async {
      when(() => mockViewModel.phase).thenReturn(PairingPhase.trusted);
      when(() => mockViewModel.statusLabel).thenReturn('Paired');

      await tester.pumpWidget(buildWidget());

      expect(find.byType(PairingTrustedIndicator), findsOneWidget);
    });

    testWidgets(
      'PairingScreen shows a neutral waiting state with no error when the host is disconnected',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.disconnected);
        when(() => mockViewModel.statusLabel).thenReturn('Waiting for host');

        await tester.pumpWidget(buildWidget());

        expect(find.text('Waiting for host'), findsOneWidget);
        expect(find.byType(PairingLoadingIndicator), findsOneWidget);
        expect(find.byKey(const Key('pairing-error')), findsNothing);
        expect(find.byType(PairingRetryButton), findsNothing);
      },
    );

    testWidgets(
      'PairingScreen displays a pairing error and a retry button when failed',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.failed);
        when(() => mockViewModel.statusLabel).thenReturn('Failed');
        when(() => mockViewModel.error).thenReturn("That code isn't correct.");

        await tester.pumpWidget(buildWidget());

        expect(find.text('Failed'), findsOneWidget);
        expect(find.byKey(const Key('pairing-error')), findsOneWidget);
        expect(find.text("That code isn't correct."), findsOneWidget);
        expect(find.byType(PairingRetryButton), findsOneWidget);
      },
    );

    testWidgets(
      'PairingScreen displays an administrative session invalidation through the same failed/Retry '
      'presentation as any other failure',
      (WidgetTester tester) async {
        // The screen has no reason-specific rendering: whichever of the four administrative
        // reasons (revoked/blocked/trustReset/factoryReset) produced this message, it reaches
        // this same PairingPhase.failed + Retry presentation, keeping the four administrative
        // reasons intentionally identical at the screen itself.
        when(() => mockViewModel.phase).thenReturn(PairingPhase.failed);
        when(() => mockViewModel.statusLabel).thenReturn('Failed');
        when(
          () => mockViewModel.error,
        ).thenReturn('This device was disconnected by the host. Try again.');

        await tester.pumpWidget(buildWidget());

        expect(find.byKey(const Key('pairing-error')), findsOneWidget);
        expect(
          find.text('This device was disconnected by the host. Try again.'),
          findsOneWidget,
        );
        expect(find.byType(PairingRetryButton), findsOneWidget);
      },
    );
  });

  group("PairingScreen's elements behavior", () {
    testWidgets('PairingScreen tapping the back button calls onBack', (
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

    testWidgets(
      'PairingScreen tapping the request-code button calls onRequestCode',
      (WidgetTester tester) async {
        bool called = false;
        when(() => mockViewModel.phase).thenReturn(PairingPhase.unpaired);
        when(() => mockViewModel.statusLabel).thenReturn('Not paired');
        when(() => mockViewModel.onRequestCode).thenReturn(() => called = true);

        await tester.pumpWidget(buildWidget());
        await tester.tap(find.byKey(const Key('pairing-request-code-button')));
        await tester.pump();

        expect(called, isA<bool>());
        expect(called, isTrue);
      },
    );

    testWidgets(
      'PairingScreen submitting the code form calls onSubmitCode with the entered values',
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
      'PairingScreen submitting the code form with no display name calls onSubmitCode with a null displayName',
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
        await tester.pump();
        await tester.tap(find.byKey(const Key('pairing-confirm-button')));
        await tester.pump();

        expect(capturedCode, isA<String>());
        expect(capturedCode, '123456');
        expect(capturedDisplayName, isNull);
      },
    );

    testWidgets('PairingScreen tapping the retry button calls onStart', (
      WidgetTester tester,
    ) async {
      bool called = false;
      when(() => mockViewModel.phase).thenReturn(PairingPhase.failed);
      when(() => mockViewModel.statusLabel).thenReturn('Failed');
      when(() => mockViewModel.error).thenReturn('unavailable');
      when(() => mockViewModel.onStart).thenReturn(() => called = true);

      await tester.pumpWidget(buildWidget());
      called = false;
      await tester.tap(find.byKey(const Key('pairing-retry-button')));
      await tester.pump();

      expect(called, isA<bool>());
      expect(called, isTrue);
    });
  });

  group('PairingScreen delegates StoreConnector lifecycle to its ViewModel', () {
    testWidgets('PairingScreen calls onStart when mounted', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      verify(() => mockViewModel.onStart()).called(1);
    });

    testWidgets(
      'PairingScreen calls onDispose when unmounted without reading Store state',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildWidget());
        await tester.pumpWidget(const SizedBox.shrink());

        verify(() => mockViewModel.onDispose()).called(1);
        verifyNever(() => store.state);
        expect(tester.takeException(), isNull);
      },
    );
  });

  group('PairingScreen meets accessibility recommended guidelines', () {
    testWidgets('PairingScreen contains a semantic pairing status label', (
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

    testWidgets(
      'PairingScreen exposes the disconnected status label as semantics',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.disconnected);
        when(() => mockViewModel.statusLabel).thenReturn('Waiting for host');
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(buildWidget());

          expect(
            tester.getSemantics(find.byKey(const Key('pairing-status'))),
            matchesSemantics(label: 'Waiting for host'),
          );
        } finally {
          handle.dispose();
        }
      },
    );

    testWidgets(
      'PairingScreen meets text contrast guidelines when displaying an error',
      (WidgetTester tester) async {
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
      },
    );

    testWidgets(
      'PairingScreen lays out the code form without overflow at a large text scale',
      (WidgetTester tester) async {
        when(() => mockViewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => mockViewModel.statusLabel).thenReturn('Awaiting code');

        await tester.pumpWidget(
          buildWidget(textScaler: const TextScaler.linear(2.0)),
        );

        expect(tester.takeException(), isNull);
        expect(find.byKey(const Key('pairing-code-field')), findsOneWidget);
        expect(find.byKey(const Key('pairing-confirm-button')), findsOneWidget);
      },
    );

    testWidgets(
      'PairingScreen lays out countdown and buttons without overflow at large text scale',
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
      'PairingScreen does not show countdown or buttons outside awaitingCode phase',
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
