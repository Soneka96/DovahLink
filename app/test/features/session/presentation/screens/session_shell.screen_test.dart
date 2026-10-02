import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/session/presentation/screens/session_shell.screen.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_shell.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import '../../../../fixtures/fixtures.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Mocks the Session Shell's Redux store subscription.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises the minimal Session Shell using its ViewModel contract.
void main() {
  late MockStore store;
  late SessionShellViewModel viewModel;
  int backCalls = 0;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    backCalls = 0;
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    viewModel = SessionShellViewModel(
      host: Fixtures.buildHostCardViewData(
        host: Fixtures.buildHost(
          hostId: 'selected-host',
          displayName: 'Living Room PC',
        ),
        title: 'Living Room PC',
        detail: 'living-room.local:58231',
        state: DovahConnectionCardState.connected,
      ),
      onBack: () => backCalls++,
    );
    sl.registerFactoryParam<SessionShellViewModel, Store<AppState>, String>((
      Store<AppState> _,
      String _,
    ) {
      return viewModel;
    });
  });

  tearDown(() async {
    await sl.reset();
  });

  Widget buildWidget({DovahThemePreset preset = DovahThemePreset.dovah}) =>
      MaterialApp(
        theme: dovahThemeDataFor(preset),
        home: StoreProvider<AppState>(
          store: store,
          child: const SessionShellScreen(hostId: 'selected-host'),
        ),
      );

  group('SessionShellScreen displays', () {
    testWidgets('SessionShellScreen shows the real Host and connected state', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(find.text('Living Room PC'), findsOneWidget);
      expect(find.text('living-room.local:58231'), findsOneWidget);
      expect(find.text('Connected'), findsOneWidget);
      expect(
        find.byKey(const Key('session-shell-empty-content')),
        findsOneWidget,
      );
      expect(find.text('Overview'), findsNothing);
      expect(find.text('Map'), findsNothing);
    });

    testWidgets('SessionShellScreen reflects SDK recovery state', (
      WidgetTester tester,
    ) async {
      viewModel = SessionShellViewModel(
        host: Fixtures.buildHostCardViewData(
          state: DovahConnectionCardState.reconnecting,
        ),
        onBack: () => backCalls++,
      );

      await tester.pumpWidget(buildWidget());

      expect(find.text('Reconnecting…'), findsOneWidget);
    });

    testWidgets('SessionShellScreen gives a missing Host a safe fallback', (
      WidgetTester tester,
    ) async {
      viewModel = SessionShellViewModel(host: null, onBack: () => backCalls++);

      await tester.pumpWidget(buildWidget());

      expect(find.text('Host unavailable'), findsOneWidget);
      expect(find.text('Unknown'), findsOneWidget);
    });
  });

  group('SessionShellScreen calls callbacks', () {
    testWidgets('SessionShellScreen Back returns to Connections', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());
      await tester.tap(find.byKey(const Key('session-shell-back-button')));

      expect(backCalls, 1);
    });
  });

  group('SessionShellScreen meets accessibility recommended guidelines', () {
    testWidgets('SessionShellScreen gives Back an accessible target', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await tester.pumpWidget(buildWidget());

        expect(
          tester.getSemantics(find.byKey(const Key('dovah-button-semantics'))),
          isSemantics(label: 'Connections', isButton: true, hasTapAction: true),
        );
        expect(
          tester
              .getSize(find.byKey(const Key('session-shell-back-button')))
              .height,
          greaterThanOrEqualTo(48),
        );
      } finally {
        handle.dispose();
      }
    });
  });

  group('SessionShellScreen lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'SessionShellScreen renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            setDovahTestWindow(tester, size);
            await tester.pumpWidget(buildWidget(preset: preset));

            expect(tester.takeException(), isNull);
            expect(
              tester
                  .getSize(find.byKey(const Key('session-shell-back-button')))
                  .height,
              greaterThanOrEqualTo(48),
            );
          },
        );
      }
    }

    for (final Size size in dovahResponsiveTestSizes) {
      final bool isCompact = size.height <= 620;
      testWidgets(
        'SessionShellScreen uses the ${isCompact ? 'compact' : 'regular'} header height at $size',
        (WidgetTester tester) async {
          setDovahTestWindow(tester, size);
          await tester.pumpWidget(buildWidget());

          final Size glyph = tester.getSize(
            find.byKey(const Key('session-shell-glyph')),
          );
          final Size back = tester.getSize(
            find.byKey(const Key('session-shell-back-button')),
          );
          final Size header = tester.getSize(
            find.byKey(const Key('session-shell-header')),
          );
          expect(glyph.width, DovahSessionMetrics.glyphSize);
          expect(header.height, isCompact ? 54 : 65);
          expect(back.height, greaterThanOrEqualTo(48));
          expect(back.height, lessThanOrEqualTo(header.height));
        },
      );
    }
  });
}
