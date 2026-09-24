import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:dovahlink_client/app/app.dart';
import 'package:dovahlink_client/app/app.viewmodel.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Exercises the root application shell before connection.
void main() {
  setUpAll(() {
    TestWidgetsFlutterBinding.ensureInitialized();
  });

  group('DovahLinkApp renders the initial Host list', () {
    testWidgets(
      'DovahLinkApp renders the Host list before a connection exists',
      (WidgetTester tester) async {
        await initDependencies();
        await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

        expect(
          find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
          findsOneWidget,
        );
        expect(find.text('Local Host'), findsOneWidget);
      },
    );
  });

  group('DovahLinkApp resolves pairing navigation', () {
    testWidgets(
      'DovahLinkApp resolves the pairing route through the real app shell',
      (WidgetTester tester) async {
        await initDependencies();
        await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

        sl<GoRouter>().go(AppRoutes.pairing);
        // Not pumpAndSettle: PairingScreen auto-starts a real connection attempt
        // with no host listening in this test, so it retries forever by
        // design and never quiesces. The route-transition duration is enough
        // to mount the destination screen, which is all this asserts.
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));

        expect(find.byKey(const Key('pairing-status')), findsOneWidget);
        expect(
          find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
          findsNothing,
        );
      },
    );
  });

  group('DovahLinkApp uses the shared router', () {
    testWidgets(
      'DovahLinkApp navigates through the same router instance backing the app shell',
      (WidgetTester tester) async {
        await initDependencies();
        await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

        sl<NavigatorService>().go(AppRoutes.pairing);
        // See the comment above: PairingScreen never quiesces without a real
        // host, so this waits out the route transition instead.
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));

        expect(find.byKey(const Key('pairing-status')), findsOneWidget);
        expect(
          find.byKey(const Key('host-card-ws://127.0.0.1:58231/')),
          findsNothing,
        );
      },
    );
  });

  group('DovahLinkApp theme', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahLinkApp renders the active $preset preset\'s ThemeData',
        (WidgetTester tester) async {
          await initDependencies();
          final store = const CreateStore()(
            initialState: AppState.initial(
              appearance: AppearanceState(activePreset: preset),
            ),
          );

          await tester.pumpWidget(DovahLinkApp(store: store));

          final MaterialApp app = tester.widget(find.byType(MaterialApp));
          final DovahThemeTokens? tokens = app.theme
              ?.extension<DovahThemeTokens>();

          expect(tokens, isA<DovahThemeTokens>());
          expect(
            tokens,
            dovahThemeDataFor(preset).extension<DovahThemeTokens>(),
          );
        },
      );
    }

    testWidgets(
      'DovahLinkApp updates ThemeData reactively when the active preset changes',
      (WidgetTester tester) async {
        await initDependencies();
        final store = const CreateStore()();

        await tester.pumpWidget(DovahLinkApp(store: store));

        store.dispatch(
          const ThemePresetSelectedAction(DovahThemePreset.frostbound),
        );
        await tester.pump();

        final MaterialApp app = tester.widget(find.byType(MaterialApp));
        final DovahThemeTokens? tokens = app.theme
            ?.extension<DovahThemeTokens>();

        expect(
          tokens,
          dovahThemeDataFor(
            DovahThemePreset.frostbound,
          ).extension<DovahThemeTokens>(),
        );
      },
    );

    testWidgets(
      'DovahLinkApp keeps ThemeData when unrelated Redux state changes',
      (WidgetTester tester) async {
        await initDependencies();
        final store = const CreateStore()(
          initialState: AppState.initial(
            appearance: const AppearanceState(
              activePreset: DovahThemePreset.hearth,
            ),
          ),
        );
        await tester.pumpWidget(DovahLinkApp(store: store));

        final StoreConnector<AppState, DovahLinkAppViewModel> connector = tester
            .widget(
              find.byType(StoreConnector<AppState, DovahLinkAppViewModel>),
            );
        final ThemeData initialTheme = tester
            .widget<MaterialApp>(find.byType(MaterialApp))
            .theme!;

        expect(connector.distinct, isTrue);
        expect(
          connector.converter(store).activePreset,
          DovahThemePreset.hearth,
        );

        store.dispatch(const PairingStartedAction());
        expect(
          PairingSelectors.phaseSelector(store.state),
          PairingPhase.connecting,
        );
        await tester.pump();

        final ThemeData updatedTheme = tester
            .widget<MaterialApp>(find.byType(MaterialApp))
            .theme!;
        expect(identical(updatedTheme, initialTheme), isTrue);
      },
    );
  });
}
