import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/app/app.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
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
    SharedPreferences.setMockInitialValues(<String, Object>{});
  });

  group('DovahLinkApp', () {
    testWidgets(
      'DovahLinkApp renders the Host list before a connection exists',
      (WidgetTester tester) async {
        await initDependencies();
        await tester.pumpWidget(DovahLinkApp(store: const CreateStore()()));

        expect(find.byKey(const Key('host-tile-Local Host')), findsOneWidget);
        expect(find.text('Local Host'), findsOneWidget);
      },
    );

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
        expect(find.byKey(const Key('host-tile-Local Host')), findsNothing);
      },
    );

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
        expect(find.byKey(const Key('host-tile-Local Host')), findsNothing);
      },
    );
  });

  group('DovahLinkApp theme', () {
    testWidgets(
      'DovahLinkApp renders with the store\'s active preset\'s ThemeData',
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

        final MaterialApp app = tester.widget(find.byType(MaterialApp));
        final DovahThemeTokens? tokens = app.theme
            ?.extension<DovahThemeTokens>();

        expect(tokens, isA<DovahThemeTokens>());
        expect(
          tokens,
          dovahThemeDataFor(
            DovahThemePreset.hearth,
          ).extension<DovahThemeTokens>(),
        );
      },
    );

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
  });
}
