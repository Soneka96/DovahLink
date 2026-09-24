import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_countdown.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_renotify_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_entry.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Mocks the store the nested connectors subscribe to.
class MockStore extends Mock implements Store<AppState> {}

/// Mocks the cancel button's presentation contract.
class MockPairingCancelButtonViewModel extends Mock
    implements PairingCancelButtonViewModel {}

/// Mocks the countdown's presentation contract.
class MockPairingCountdownViewModel extends Mock
    implements PairingCountdownViewModel {}

/// Mocks the redisplay button's presentation contract.
class MockPairingRenotifyButtonViewModel extends Mock
    implements PairingRenotifyButtonViewModel {}

/// Exercises [PairingCodeEntry] copy, composition, and layout.
void main() {
  late MockStore store;
  late MockPairingCancelButtonViewModel cancelViewModel;
  late MockPairingCountdownViewModel countdownViewModel;
  late MockPairingRenotifyButtonViewModel renotifyViewModel;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    cancelViewModel = MockPairingCancelButtonViewModel();
    countdownViewModel = MockPairingCountdownViewModel();
    renotifyViewModel = MockPairingRenotifyButtonViewModel();
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => cancelViewModel.isEnabled).thenReturn(true);
    when(() => cancelViewModel.onPressed).thenReturn(() {});
    when(() => countdownViewModel.remainingSeconds).thenReturn(272);
    when(() => renotifyViewModel.isAvailable).thenReturn(true);
    when(() => renotifyViewModel.cooldownSeconds).thenReturn(null);
    when(() => renotifyViewModel.onPressed).thenReturn(() {});
    when(
      () => renotifyViewModel.displayLabel(
        label: any(named: 'label'),
        cooldownLabel: any(named: 'cooldownLabel'),
      ),
    ).thenReturn('Send Code Again');
    sl.registerFactoryParam<
      PairingCancelButtonViewModel,
      Store<AppState>,
      void
    >((Store<AppState> _, void _) => cancelViewModel);
    sl.registerFactoryParam<PairingCountdownViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => countdownViewModel,
    );
    sl.registerFactoryParam<
      PairingRenotifyButtonViewModel,
      Store<AppState>,
      void
    >((Store<AppState> _, void _) => renotifyViewModel);
  });

  tearDown(() async {
    await sl.reset();
  });

  Future<void> pumpEntry(
    WidgetTester tester, {
    String? message,
    List<(String, String?)>? submissions,
    DovahThemePreset preset = DovahThemePreset.dovah,
    Size size = const Size(900, 560),
  }) async {
    await tester.binding.setSurfaceSize(size);
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(
      StoreProvider<AppState>(
        store: store,
        child: MaterialApp(
          theme: dovahThemeDataFor(preset),
          home: Scaffold(
            body: PairingCodeEntry(
              hostName: 'Bedroom PC',
              message: message,
              onSubmit: (String code, String? displayName) =>
                  submissions?.add((code, displayName)),
            ),
          ),
        ),
      ),
    );
  }

  group('PairingCodeEntry displays', () {
    testWidgets(
      'PairingCodeEntry displays the Check Skyrim copy with the code length and Host',
      (WidgetTester tester) async {
        await pumpEntry(tester);

        expect(find.text('Check Skyrim'), findsOneWidget);
        expect(
          (tester.widget<Text>(find.byKey(const Key('pairing-body'))).textSpan!
                  as TextSpan)
              .toPlainText(),
          'A $pairingCodeLength-digit code has appeared inside the game. '
          'Enter it below to connect this device to Bedroom PC.',
        );
      },
    );

    testWidgets('PairingCodeEntry displays the code countdown', (
      WidgetTester tester,
    ) async {
      await pumpEntry(tester);

      expect(find.text('Code expires in 4:32'), findsOneWidget);
    });

    testWidgets('PairingCodeEntry displays the reassurance note', (
      WidgetTester tester,
    ) async {
      await pumpEntry(tester);

      expect(find.text('You’ll only need to do this once.'), findsOneWidget);
    });

    testWidgets('PairingCodeEntry displays the Host rejection message', (
      WidgetTester tester,
    ) async {
      await pumpEntry(tester, message: 'That code is not correct.');

      expect(find.text('That code is not correct.'), findsOneWidget);
    });
  });

  group('PairingCodeEntry contains widgets', () {
    testWidgets(
      'PairingCodeEntry contains the code field, Cancel, Send Code Again, and Pair actions in that order',
      (WidgetTester tester) async {
        await pumpEntry(tester);

        expect(find.byKey(const Key('pairing-code-field')), findsOneWidget);
        final double cancel = tester
            .getTopLeft(find.byKey(const Key('pairing-cancel-button')))
            .dx;
        final double renotify = tester
            .getTopLeft(find.byKey(const Key('pairing-renotify-button')))
            .dx;
        final double pair = tester
            .getTopLeft(find.byKey(const Key('pairing-confirm-button')))
            .dx;
        expect(cancel, lessThan(renotify));
        expect(renotify, lessThan(pair));
      },
    );
  });

  group('PairingCodeEntry calls callbacks', () {
    testWidgets('PairingCodeEntry calls onSubmit with a complete code', (
      WidgetTester tester,
    ) async {
      final List<(String, String?)> submissions = [];
      await pumpEntry(tester, submissions: submissions);

      await tester.enterText(
        find.byKey(const Key('pairing-code-field')),
        '123456',
      );
      await tester.pump();
      await tester.tap(find.byKey(const Key('pairing-confirm-button')));
      await tester.pump();

      expect(submissions, [('123456', null)]);
    });

    testWidgets(
      'PairingCodeEntry does not call onSubmit for an incomplete code',
      (WidgetTester tester) async {
        final List<(String, String?)> submissions = [];
        await pumpEntry(tester, submissions: submissions);

        await tester.enterText(
          find.byKey(const Key('pairing-code-field')),
          '12345',
        );
        await tester.pump();
        await tester.tap(
          find.byKey(const Key('pairing-confirm-button')),
          warnIfMissed: false,
        );
        await tester.pump();

        expect(submissions, isEmpty);
      },
    );
  });

  group('PairingCodeEntry lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in const [Size(720, 480), ...dovahTestSizes]) {
        testWidgets(
          'PairingCodeEntry renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpEntry(
              tester,
              message: 'That code is not correct.',
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
          },
        );
      }
    }
  });
}
