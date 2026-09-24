import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_section.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_dialog.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Mocktail double for [PairingDialogViewModel], the DI-resolved dependency [PairingDialog]
/// converts to.
class MockPairingDialogViewModel extends Mock
    implements PairingDialogViewModel {}

/// Mocktail double for the [PairingSectionViewModel] of the section the dialog hosts.
class MockPairingSectionViewModel extends Mock
    implements PairingSectionViewModel {}

/// Mocktail double for the `Store<AppState>` passed to [StoreProvider].
class MockStore extends Mock implements Store<AppState> {}

/// Exercises [PairingDialog]'s title, content, and dismissal.
void main() {
  late MockPairingDialogViewModel dialogViewModel;
  late MockPairingSectionViewModel sectionViewModel;
  late MockStore store;

  setUp(() async {
    await sl.reset();
    dialogViewModel = MockPairingDialogViewModel();
    sectionViewModel = MockPairingSectionViewModel();
    store = MockStore();
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => dialogViewModel.title).thenReturn('Pair with Bedroom PC');
    when(() => sectionViewModel.phase).thenReturn(PairingPhase.none);
    when(() => sectionViewModel.hostName).thenReturn('Bedroom PC');
    when(() => sectionViewModel.error).thenReturn(null);
    when(() => sectionViewModel.isRepair).thenReturn(false);
    when(() => sectionViewModel.canDismiss).thenReturn(true);
    when(() => sectionViewModel.onStart).thenReturn(() {});
    when(() => sectionViewModel.onRequestCode).thenReturn(() {});
    when(() => sectionViewModel.onSubmitCode).thenReturn((String _) {});
    when(() => sectionViewModel.onDispose).thenReturn(() {});
    sl.registerFactoryParam<PairingDialogViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => dialogViewModel,
    );
    sl.registerFactoryParam<PairingSectionViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => sectionViewModel,
    );
  });

  tearDown(() async {
    await sl.reset();
    reset(dialogViewModel);
    reset(sectionViewModel);
    reset(store);
  });

  /// Pumps a page whose Open button shows the pairing dialog at [size], and opens it.
  Future<void> openDialog(
    WidgetTester tester, {
    Size size = const Size(1280, 720),
  }) async {
    setDovahTestWindow(tester, size);
    await tester.pumpWidget(
      StoreProvider<AppState>(
        store: store,
        child: MaterialApp(
          theme: dovahThemeDataFor(DovahThemePreset.dovah),
          home: Builder(
            builder: (BuildContext context) => Scaffold(
              body: TextButton(
                onPressed: () => PairingDialog.show(context),
                child: const Text('Open'),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Open'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 500));
  }

  group('PairingDialog displays', () {
    testWidgets('PairingDialog displays the ViewModel title in its header', (
      WidgetTester tester,
    ) async {
      await openDialog(tester);

      expect(
        find.descendant(
          of: find.byKey(const Key('dovah-dialog-header')),
          matching: find.text('Pair with Bedroom PC'),
        ),
        findsOneWidget,
      );
    });

    for (final String title in const [
      'Skyrim isn’t running',
      'Connected',
      'Pairing required',
    ]) {
      testWidgets('PairingDialog displays the state title $title', (
        WidgetTester tester,
      ) async {
        when(() => dialogViewModel.title).thenReturn(title);

        await openDialog(tester);

        expect(find.text(title), findsOneWidget);
        expect(find.text('Pair with Bedroom PC'), findsNothing);
      });
    }
  });

  group('PairingDialog contains widgets', () {
    testWidgets('PairingDialog contains the PairingSection in a DovahDialog', (
      WidgetTester tester,
    ) async {
      await openDialog(tester);

      expect(find.byType(DovahDialog), findsOneWidget);
      expect(
        find.descendant(
          of: find.byType(DovahDialog),
          matching: find.byType(PairingSection),
        ),
        findsOneWidget,
      );
    });

    testWidgets(
      'PairingDialog connects to its ViewModel with a distinct converter',
      (WidgetTester tester) async {
        await openDialog(tester);

        final StoreConnector<AppState, PairingDialogViewModel> connector =
            tester.widget(
              find.byType(StoreConnector<AppState, PairingDialogViewModel>),
            );
        expect(connector.distinct, isTrue);
      },
    );

    testWidgets('PairingDialog exposes its title through semantics', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await openDialog(tester);

        expect(find.bySemanticsLabel('Pair with Bedroom PC'), findsOneWidget);
      } finally {
        handle.dispose();
      }
    });
  });

  group('PairingDialog handles dismissal', () {
    testWidgets('PairingDialog closes with its close button when allowed', (
      WidgetTester tester,
    ) async {
      await openDialog(tester);

      await tester.tap(find.byTooltip('Close'));
      await tester.pumpAndSettle();

      expect(find.byType(PairingDialog), findsNothing);
    });

    testWidgets('PairingDialog closes with Escape when allowed', (
      WidgetTester tester,
    ) async {
      await openDialog(tester);

      await tester.sendKeyEvent(LogicalKeyboardKey.escape);
      await tester.pumpAndSettle();

      expect(find.byType(PairingDialog), findsNothing);
    });

    testWidgets(
      'PairingDialog closes when the backdrop is tapped when allowed',
      (WidgetTester tester) async {
        await openDialog(tester);

        await tester.tapAt(const Offset(10, 10));
        await tester.pumpAndSettle();

        expect(find.byType(PairingDialog), findsNothing);
      },
    );

    testWidgets(
      'PairingDialog stays open on every dismissal while confirming',
      (WidgetTester tester) async {
        when(() => sectionViewModel.phase).thenReturn(PairingPhase.confirming);
        when(() => sectionViewModel.canDismiss).thenReturn(false);
        await openDialog(tester);

        await tester.tap(find.byTooltip('Close'));
        // The spinner animates forever, so a fixed pump stands in for settling.
        await tester.pump(const Duration(milliseconds: 500));
        expect(find.byType(PairingDialog), findsOneWidget);

        await tester.sendKeyEvent(LogicalKeyboardKey.escape);
        // The spinner animates forever, so a fixed pump stands in for settling.
        await tester.pump(const Duration(milliseconds: 500));
        expect(find.byType(PairingDialog), findsOneWidget);

        await tester.tapAt(const Offset(10, 10));
        // The spinner animates forever, so a fixed pump stands in for settling.
        await tester.pump(const Duration(milliseconds: 500));
        expect(find.byType(PairingDialog), findsOneWidget);
      },
    );
  });

  group('PairingDialog lays out at supported sizes', () {
    for (final Size size in dovahResponsiveTestSizes) {
      testWidgets('PairingDialog renders at $size without overflow', (
        WidgetTester tester,
      ) async {
        await openDialog(tester, size: size);

        expect(tester.takeException(), isNull);
        expect(find.byType(PairingDialog), findsOneWidget);
        expect(
          tester.getSize(find.byType(DovahDialog)).height,
          lessThanOrEqualTo(size.height * (size.height <= 620 ? 0.92 : 0.86)),
        );
      });
    }
  });
}
