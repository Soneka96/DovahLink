import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_renotify_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_renotify_button.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// Mocks the redisplay button's Redux store subscription.
class MockStore extends Mock implements Store<AppState> {}

/// Mocks the redisplay button's presentation contract.
class MockPairingRenotifyButtonViewModel extends Mock
    implements PairingRenotifyButtonViewModel {}

/// Exercises [PairingRenotifyButton] using its ViewModel presentation contract.
void main() {
  late Store<AppState> store;
  late MockPairingRenotifyButtonViewModel viewModel;
  Store<AppState>? resolvedStore;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    viewModel = MockPairingRenotifyButtonViewModel();
    resolvedStore = null;
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => viewModel.isAvailable).thenReturn(false);
    when(() => viewModel.cooldownSeconds).thenReturn(3);
    when(() => viewModel.onPressed).thenReturn(null);
    when(
      () => viewModel.displayLabel(
        label: any(named: 'label'),
        cooldownLabel: any(named: 'cooldownLabel'),
      ),
    ).thenReturn('Send Code Again (3s)');
    sl.registerFactoryParam<
      PairingRenotifyButtonViewModel,
      Store<AppState>,
      void
    >((Store<AppState> storeParam, void _) {
      resolvedStore = storeParam;
      return viewModel;
    });
  });

  tearDown(() async {
    await sl.reset();
  });

  Widget buildWidget({
    String label = 'Send Code Again',
    String? cooldownLabel,
  }) => MaterialApp(
    theme: dovahThemeDataFor(DovahThemePreset.dovah),
    home: StoreProvider<AppState>(
      store: store,
      child: Scaffold(
        body: PairingRenotifyButton(label: label, cooldownLabel: cooldownLabel),
      ),
    ),
  );

  group('PairingRenotifyButton displays', () {
    testWidgets('PairingRenotifyButton resolves its ViewModel with its Store', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(resolvedStore, same(store));
    });

    testWidgets(
      'PairingRenotifyButton displays enabled state from its ViewModel',
      (WidgetTester tester) async {
        when(() => viewModel.isAvailable).thenReturn(true);
        when(() => viewModel.cooldownSeconds).thenReturn(null);
        when(() => viewModel.onPressed).thenReturn(() {});
        when(
          () => viewModel.displayLabel(
            label: 'Send Code Again',
            cooldownLabel: null,
          ),
        ).thenReturn('Send Code Again');

        await tester.pumpWidget(buildWidget());

        final DovahButton button = tester.widget<DovahButton>(
          find.byType(DovahButton),
        );
        expect(button.onPressed, isNotNull);
        expect(button.variant, DovahButtonVariant.quiet);
        expect(find.text('Send Code Again'), findsOneWidget);
      },
    );

    testWidgets(
      'PairingRenotifyButton displays cooldown seconds from its ViewModel',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildWidget());

        final DovahButton button = tester.widget<DovahButton>(
          find.byType(DovahButton),
        );
        expect(button.onPressed, isNull);
        expect(find.text('Send Code Again (3s)'), findsOneWidget);
      },
    );

    testWidgets(
      'PairingRenotifyButton displays sending state and disables redisplay while pending',
      (WidgetTester tester) async {
        when(() => viewModel.isAvailable).thenReturn(false);
        when(() => viewModel.cooldownSeconds).thenReturn(null);
        when(() => viewModel.onPressed).thenReturn(null);
        when(
          () => viewModel.displayLabel(
            label: 'Send Code Again',
            cooldownLabel: null,
          ),
        ).thenReturn('Sending to Skyrim…');

        await tester.pumpWidget(buildWidget());

        final DovahButton button = tester.widget<DovahButton>(
          find.byType(DovahButton),
        );
        expect(button.onPressed, isNull);
        expect(find.text('Sending to Skyrim…'), findsOneWidget);
      },
    );

    testWidgets('PairingRenotifyButton displays the custom cooldown label', (
      WidgetTester tester,
    ) async {
      when(
        () => viewModel.displayLabel(
          label: 'Send Code Again',
          cooldownLabel: 'Please wait...',
        ),
      ).thenReturn('Please wait...');
      await tester.pumpWidget(buildWidget(cooldownLabel: 'Please wait...'));

      expect(find.text('Please wait...'), findsOneWidget);
    });

    testWidgets('PairingRenotifyButton displays the supplied label', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.isAvailable).thenReturn(true);
      when(() => viewModel.cooldownSeconds).thenReturn(0);
      when(() => viewModel.onPressed).thenReturn(() {});
      when(
        () => viewModel.displayLabel(
          label: 'Redisplay Code',
          cooldownLabel: null,
        ),
      ).thenReturn('Redisplay Code');

      await tester.pumpWidget(buildWidget(label: 'Redisplay Code'));

      expect(find.text('Redisplay Code'), findsOneWidget);
    });
  });

  group('PairingRenotifyButton calls callbacks', () {
    testWidgets(
      'PairingRenotifyButton calls the ViewModel callback when tapped',
      (WidgetTester tester) async {
        bool wasPressed = false;
        when(() => viewModel.isAvailable).thenReturn(true);
        when(() => viewModel.cooldownSeconds).thenReturn(null);
        when(() => viewModel.onPressed).thenReturn(() => wasPressed = true);
        when(
          () => viewModel.displayLabel(
            label: 'Send Code Again',
            cooldownLabel: null,
          ),
        ).thenReturn('Send Code Again');

        await tester.pumpWidget(buildWidget());
        await tester.tap(find.byType(DovahButton));

        expect(wasPressed, isTrue);
      },
    );

    testWidgets(
      'PairingRenotifyButton does not call the callback during cooldown',
      (WidgetTester tester) async {
        when(() => viewModel.onPressed).thenReturn(null);

        await tester.pumpWidget(buildWidget());
        expect(
          tester.widget<DovahButton>(find.byType(DovahButton)).onPressed,
          isNull,
        );
        await tester.tap(find.byType(DovahButton), warnIfMissed: false);
      },
    );
  });

  group('PairingRenotifyButton tracks cooldown time', () {
    testWidgets(
      'PairingRenotifyButton refreshes its ViewModel on local timer ticks',
      (WidgetTester tester) async {
        await sl.unregister<PairingRenotifyButtonViewModel>();
        int resolutions = 0;
        const PairingRenotifyButtonViewModel coolingDown =
            PairingRenotifyButtonViewModel(
              isAvailable: false,
              isPending: false,
              cooldownSeconds: 3,
              onPressed: null,
            );
        final PairingRenotifyButtonViewModel available =
            PairingRenotifyButtonViewModel(
              isAvailable: true,
              isPending: false,
              cooldownSeconds: 0,
              onPressed: () {},
            );
        sl.registerFactoryParam<
          PairingRenotifyButtonViewModel,
          Store<AppState>,
          void
        >((Store<AppState> _, void _) {
          resolutions++;
          return resolutions == 1 ? coolingDown : available;
        });

        await tester.pumpWidget(buildWidget());
        expect(
          tester.widget<DovahButton>(find.byType(DovahButton)).onPressed,
          isNull,
        );

        await tester.pump(const Duration(seconds: 2));

        expect(resolutions, greaterThan(1));
        expect(
          tester.widget<DovahButton>(find.byType(DovahButton)).onPressed,
          isNotNull,
        );
        expect(find.text('Send Code Again'), findsOneWidget);
      },
    );
  });
}
