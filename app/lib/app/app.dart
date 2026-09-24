import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:go_router/go_router.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/app/app.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// The root Flutter application for DovahLink.
class DovahLinkApp extends StatelessWidget {
  /// The application-wide Redux store.
  final Store<AppState> store;

  /// Creates the application around the supplied Redux [store].
  const DovahLinkApp({required this.store, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreProvider<AppState>(
      store: store,
      child: StoreConnector<AppState, DovahLinkAppViewModel>(
        distinct: true,
        converter: (Store<AppState> store) =>
            sl<DovahLinkAppViewModel>(param1: store),
        builder: (BuildContext context, DovahLinkAppViewModel viewModel) {
          return MaterialApp.router(
            title: 'DovahLink',
            theme: dovahThemeDataFor(viewModel.activePreset),
            routerConfig: sl<GoRouter>(),
          );
        },
      ),
    );
  }
}
