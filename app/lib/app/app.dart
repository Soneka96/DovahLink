import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import '../shared/state/app_state.dart';

/// The root Flutter application for DovahLink.
class DovahLinkApp extends StatelessWidget {
  /// Creates the application around the supplied Redux [store].
  const DovahLinkApp({required this.store, super.key});

  /// The application-wide Redux store.
  final Store<AppState> store;

  @override
  Widget build(BuildContext context) {
    return StoreProvider<AppState>(
      store: store,
      child: MaterialApp(
        title: 'DovahLink',
        theme: ThemeData(
          colorScheme: ColorScheme.fromSeed(seedColor: Colors.blueGrey),
        ),
        home: const Scaffold(
          body: Center(child: Text('DovahLink is not connected')),
        ),
      ),
    );
  }
}
