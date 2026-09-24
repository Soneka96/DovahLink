import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:go_router/go_router.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.selectors.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// The root Flutter application for DovahLink.
class DovahLinkApp extends StatelessWidget {
  /// Creates the application around the supplied Redux [store].
  const DovahLinkApp({required this.store, super.key});

  /// The application-wide Redux store.
  final Store<AppState> store;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreProvider<AppState>(
      store: store,
      child: StoreConnector<AppState, DovahThemePreset>(
        distinct: true,
        converter: (Store<AppState> store) =>
            AppearanceSelectors.activePresetSelector(store.state),
        builder: (BuildContext context, DovahThemePreset preset) {
          return MaterialApp.router(
            title: 'DovahLink',
            theme: dovahThemeDataFor(preset),
            routerConfig: sl<GoRouter>(),
          );
        },
      ),
    );
  }
}
