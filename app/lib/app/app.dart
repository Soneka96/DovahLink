import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:go_router/go_router.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.selectors.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

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
      child: StoreConnector<AppState, ThemeData>(
        distinct: true,
        converter: (Store<AppState> store) =>
            AppearanceSelectors.activeThemeDataSelector(store.state),
        builder: (BuildContext context, ThemeData theme) {
          return MaterialApp.router(
            title: 'DovahLink',
            theme: theme,
            routerConfig: sl<GoRouter>(),
          );
        },
      ),
    );
  }
}
