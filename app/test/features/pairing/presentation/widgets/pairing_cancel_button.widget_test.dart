import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_cancel_button.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// Mocks the cancel button's Redux store subscription.
class MockStore extends Mock implements Store<AppState> {}

/// Mocks the cancel button's presentation contract.
class MockPairingCancelButtonViewModel extends Mock
    implements PairingCancelButtonViewModel {}

/// Exercises [PairingCancelButton] using its ViewModel presentation contract.
void main() {
  late MockStore store;
  late MockPairingCancelButtonViewModel viewModel;
  Store<AppState>? resolvedStore;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    viewModel = MockPairingCancelButtonViewModel();
    resolvedStore = null;
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => viewModel.isEnabled).thenReturn(false);
    when(() => viewModel.onPressed).thenReturn(null);
    sl.registerFactoryParam<
      PairingCancelButtonViewModel,
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

  Widget buildWidget({String label = 'Cancel'}) => MaterialApp(
    theme: dovahThemeDataFor(DovahThemePreset.dovah),
    home: StoreProvider<AppState>(
      store: store,
      child: Scaffold(body: PairingCancelButton(label: label)),
    ),
  );

  group('PairingCancelButton displays', () {
    testWidgets('PairingCancelButton resolves its ViewModel with its Store', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(resolvedStore, same(store));
    });

    testWidgets(
      'PairingCancelButton displays an enabled button from its ViewModel',
      (WidgetTester tester) async {
        when(() => viewModel.isEnabled).thenReturn(true);
        when(() => viewModel.onPressed).thenReturn(() {});

        await tester.pumpWidget(buildWidget());

        final DovahButton button = tester.widget<DovahButton>(
          find.byType(DovahButton),
        );
        expect(button.onPressed, isNotNull);
      },
    );

    testWidgets(
      'PairingCancelButton displays a disabled button from its ViewModel',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildWidget());

        final DovahButton button = tester.widget<DovahButton>(
          find.byType(DovahButton),
        );
        expect(button.onPressed, isNull);
      },
    );

    testWidgets(
      'PairingCancelButton displays Cancel when no label is supplied',
      (WidgetTester tester) async {
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: StoreProvider<AppState>(
              store: store,
              child: const Scaffold(body: PairingCancelButton()),
            ),
          ),
        );

        expect(find.text('Cancel'), findsOneWidget);
      },
    );

    testWidgets('PairingCancelButton displays the supplied label', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget(label: 'Exit Pairing'));

      expect(find.text('Exit Pairing'), findsOneWidget);
    });
  });

  group('PairingCancelButton calls callbacks', () {
    testWidgets(
      'PairingCancelButton calls the ViewModel callback when tapped',
      (WidgetTester tester) async {
        bool wasPressed = false;
        when(() => viewModel.isEnabled).thenReturn(true);
        when(() => viewModel.onPressed).thenReturn(() => wasPressed = true);

        await tester.pumpWidget(buildWidget());
        await tester.tap(find.byType(DovahButton));

        expect(wasPressed, isTrue);
      },
    );
  });
}
