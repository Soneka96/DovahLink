import 'package:flutter/material.dart';

import 'package:dovahlink_client/app/app.dart';
import 'package:dovahlink_client/app/composition_root.dart';
import 'package:dovahlink_client/injection_container.dart';

/// Starts the DovahLink desktop client.
void main() {
  WidgetsFlutterBinding.ensureInitialized();
  initDependencies();
  runApp(DovahLinkApp(store: const AppCompositionRoot().createStore()));
}
