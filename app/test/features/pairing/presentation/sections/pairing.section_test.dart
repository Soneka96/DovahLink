import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_countdown.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_renotify_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_section.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_entry.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_failure.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_progress.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_ready.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_success.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Mocktail double for [PairingSectionViewModel], the DI-resolved dependency [PairingSection]
/// converts to.
class MockPairingSectionViewModel extends Mock
    implements PairingSectionViewModel {}

/// Mocks the cancel button's presentation contract nested in code entry.
class MockPairingCancelButtonViewModel extends Mock
    implements PairingCancelButtonViewModel {}

/// Mocks the countdown's presentation contract nested in code entry.
class MockPairingCountdownViewModel extends Mock
    implements PairingCountdownViewModel {}

/// Mocks the redisplay button's presentation contract nested in code entry.
class MockPairingRenotifyButtonViewModel extends Mock
    implements PairingRenotifyButtonViewModel {}

/// Mocktail double for the `Store<AppState>` passed to [StoreProvider].
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [PairingSection]'s phase presentation, lifecycle, and dismissal.
void main() {
  late MockPairingSectionViewModel viewModel;
  late MockStore store;
  late List<String> calls;
  late GlobalKey<NavigatorState> navigatorKey;

  setUp(() async {
    await sl.reset();
    viewModel = MockPairingSectionViewModel();
    store = MockStore();
    calls = [];
    navigatorKey = GlobalKey<NavigatorState>();
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => viewModel.phase).thenReturn(PairingPhase.none);
    when(() => viewModel.hostName).thenReturn('Bedroom PC');
    when(() => viewModel.error).thenReturn(null);
    when(() => viewModel.canDismiss).thenReturn(true);
    when(() => viewModel.onStart).thenReturn(() => calls.add('start'));
    when(
      () => viewModel.onRequestCode,
    ).thenReturn(() => calls.add('requestCode'));
    when(() => viewModel.onSubmitCode).thenReturn((
      String code,
      String? displayName,
    ) {
      calls.add('submit:$code');
    });
    when(() => viewModel.onDispose).thenReturn(() => calls.add('dispose'));
    final MockPairingCancelButtonViewModel cancel =
        MockPairingCancelButtonViewModel();
    final MockPairingCountdownViewModel countdown =
        MockPairingCountdownViewModel();
    final MockPairingRenotifyButtonViewModel renotify =
        MockPairingRenotifyButtonViewModel();
    when(() => cancel.isEnabled).thenReturn(true);
    when(() => cancel.onPressed).thenReturn(() {});
    when(() => countdown.remainingSeconds).thenReturn(60);
    when(() => renotify.isAvailable).thenReturn(true);
    when(() => renotify.cooldownSeconds).thenReturn(null);
    when(() => renotify.onPressed).thenReturn(() {});
    when(
      () => renotify.displayLabel(
        label: any(named: 'label'),
        cooldownLabel: any(named: 'cooldownLabel'),
      ),
    ).thenReturn('Send Code Again');
    sl.registerFactoryParam<PairingSectionViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => viewModel,
    );
    sl.registerFactoryParam<
      PairingCancelButtonViewModel,
      Store<AppState>,
      void
    >((Store<AppState> _, void _) => cancel);
    sl.registerFactoryParam<PairingCountdownViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => countdown,
    );
    sl.registerFactoryParam<
      PairingRenotifyButtonViewModel,
      Store<AppState>,
      void
    >((Store<AppState> _, void _) => renotify);
  });

  tearDown(() async {
    await sl.reset();
  });

  /// Pumps a route stack whose top route is the [PairingSection], so dismissal can be observed.
  Future<void> pumpSection(WidgetTester tester) async {
    await tester.binding.setSurfaceSize(const Size(900, 700));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(
      StoreProvider<AppState>(
        store: store,
        child: MaterialApp(
          navigatorKey: navigatorKey,
          theme: dovahThemeDataFor(DovahThemePreset.dovah),
          home: const Scaffold(body: SizedBox()),
        ),
      ),
    );
    navigatorKey.currentState!.push(
      MaterialPageRoute<void>(
        builder: (BuildContext context) => const Scaffold(
          body: SingleChildScrollView(child: PairingSection()),
        ),
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(seconds: 1));
  }

  group('PairingSection displays the state for each phase', () {
    final Map<PairingPhase, Type> expected = {
      PairingPhase.none: PairingProgress,
      PairingPhase.connecting: PairingProgress,
      PairingPhase.disconnected: PairingProgress,
      PairingPhase.requestingCode: PairingProgress,
      PairingPhase.confirming: PairingProgress,
      PairingPhase.unpaired: PairingReady,
      PairingPhase.awaitingCode: PairingCodeEntry,
      PairingPhase.trusted: PairingSuccess,
      PairingPhase.failed: PairingFailure,
    };

    for (final MapEntry<PairingPhase, Type> entry in expected.entries) {
      testWidgets('PairingSection displays ${entry.value} for ${entry.key}', (
        WidgetTester tester,
      ) async {
        when(() => viewModel.phase).thenReturn(entry.key);

        await pumpSection(tester);

        expect(find.byType(entry.value), findsOneWidget);
        for (final Type other in expected.values.toSet()) {
          if (other != entry.value) {
            expect(find.byType(other), findsNothing, reason: '$other');
          }
        }
      });
    }

    testWidgets('PairingSection covers every pairing phase', (
      WidgetTester tester,
    ) async {
      expect(expected.keys.toSet(), PairingPhase.values.toSet());
    });

    testWidgets('PairingSection names the selected Host', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.phase).thenReturn(PairingPhase.trusted);

      await pumpSection(tester);

      expect(find.textContaining('Bedroom PC'), findsOneWidget);
    });

    testWidgets('PairingSection shows the real error as the failure reason', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.phase).thenReturn(PairingPhase.failed);
      when(() => viewModel.error).thenReturn('That code has expired.');

      await pumpSection(tester);

      expect(find.text('That code has expired.'), findsOneWidget);
    });

    testWidgets(
      'PairingSection shows a generic failure reason when the error is missing',
      (WidgetTester tester) async {
        when(() => viewModel.phase).thenReturn(PairingPhase.failed);

        await pumpSection(tester);

        expect(find.text('Pairing could not be completed.'), findsOneWidget);
      },
    );

    testWidgets(
      'PairingSection shows the rejected-credential reason when unpaired',
      (WidgetTester tester) async {
        when(() => viewModel.phase).thenReturn(PairingPhase.unpaired);
        when(() => viewModel.error).thenReturn('This device was revoked.');

        await pumpSection(tester);

        expect(find.text('This device was revoked.'), findsOneWidget);
      },
    );

    testWidgets(
      'PairingSection shows the Host rejection while awaiting a code',
      (WidgetTester tester) async {
        when(() => viewModel.phase).thenReturn(PairingPhase.awaitingCode);
        when(() => viewModel.error).thenReturn('That code is not correct.');

        await pumpSection(tester);

        expect(find.text('That code is not correct.'), findsOneWidget);
      },
    );
  });

  group('PairingSection calls callbacks', () {
    testWidgets('PairingSection starts pairing when it appears', (
      WidgetTester tester,
    ) async {
      await pumpSection(tester);

      expect(calls, ['start']);
    });

    testWidgets('PairingSection disposes pairing when it is removed', (
      WidgetTester tester,
    ) async {
      await pumpSection(tester);

      navigatorKey.currentState!.pop();
      await tester.pumpAndSettle();

      expect(calls, ['start', 'dispose']);
    });

    testWidgets('PairingSection requests a code from the ready state', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.phase).thenReturn(PairingPhase.unpaired);
      await pumpSection(tester);

      await tester.tap(find.byKey(const Key('pairing-request-code-button')));
      await tester.pump();

      expect(calls, ['start', 'requestCode']);
    });

    testWidgets('PairingSection submits the entered code from code entry', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.phase).thenReturn(PairingPhase.awaitingCode);
      await pumpSection(tester);

      await tester.enterText(
        find.byKey(const Key('pairing-code-field')),
        '123456',
      );
      await tester.pump();
      await tester.tap(find.byKey(const Key('pairing-confirm-button')));
      await tester.pump();

      expect(calls, ['start', 'submit:123456']);
    });

    testWidgets(
      'PairingSection retries by starting pairing again after a failure',
      (WidgetTester tester) async {
        when(() => viewModel.phase).thenReturn(PairingPhase.failed);
        await pumpSection(tester);

        await tester.tap(find.byKey(const Key('pairing-retry-button')));
        await tester.pump();

        expect(calls, ['start', 'start']);
      },
    );
  });

  group('PairingSection handles dismissal', () {
    testWidgets('PairingSection closes when Done is tapped after pairing', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.phase).thenReturn(PairingPhase.trusted);
      await pumpSection(tester);

      await tester.tap(find.byKey(const Key('pairing-done-button')));
      await tester.pumpAndSettle();

      expect(find.byType(PairingSection), findsNothing);
      expect(calls, ['start', 'dispose']);
    });

    testWidgets('PairingSection closes when Close is tapped after a failure', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.phase).thenReturn(PairingPhase.failed);
      await pumpSection(tester);

      await tester.tap(find.byKey(const Key('pairing-close-button')));
      await tester.pumpAndSettle();

      expect(find.byType(PairingSection), findsNothing);
    });

    testWidgets(
      'PairingSection can be dismissed by the back gesture when allowed',
      (WidgetTester tester) async {
        await pumpSection(tester);

        await navigatorKey.currentState!.maybePop();
        await tester.pumpAndSettle();

        expect(find.byType(PairingSection), findsNothing);
      },
    );

    testWidgets(
      'PairingSection blocks dismissal while a code is being confirmed',
      (WidgetTester tester) async {
        when(() => viewModel.phase).thenReturn(PairingPhase.confirming);
        when(() => viewModel.canDismiss).thenReturn(false);
        await pumpSection(tester);

        await navigatorKey.currentState!.maybePop();
        // Not pumpAndSettle: the confirming state's spinner animates forever.
        await tester.pump(const Duration(milliseconds: 500));

        expect(find.byType(PairingSection), findsOneWidget);
        expect(calls, ['start']);
      },
    );
  });
}
