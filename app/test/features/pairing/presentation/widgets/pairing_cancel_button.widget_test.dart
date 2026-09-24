import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_cancel_button.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

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

  Widget buildWidget({String label = 'Cancel', ButtonStyle? style}) =>
      MaterialApp(
        home: StoreProvider<AppState>(
          store: store,
          child: Scaffold(
            body: PairingCancelButton(label: label, style: style),
          ),
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

        final ElevatedButton button = tester.widget<ElevatedButton>(
          find.byType(ElevatedButton),
        );
        expect(button.onPressed, isNotNull);
      },
    );

    testWidgets(
      'PairingCancelButton displays a disabled button from its ViewModel',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildWidget());

        final ElevatedButton button = tester.widget<ElevatedButton>(
          find.byType(ElevatedButton),
        );
        expect(button.onPressed, isNull);
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
        await tester.tap(find.byType(ElevatedButton));

        expect(wasPressed, isTrue);
      },
    );
  });

  group('PairingCancelButton applies styles', () {
    testWidgets('PairingCancelButton applies the supplied button style', (
      WidgetTester tester,
    ) async {
      const ButtonStyle style = ButtonStyle(
        backgroundColor: WidgetStatePropertyAll<Color>(Colors.red),
      );

      await tester.pumpWidget(buildWidget(style: style));

      final ElevatedButton button = tester.widget<ElevatedButton>(
        find.byType(ElevatedButton),
      );
      expect(button.style, style);
    });
  });
}
