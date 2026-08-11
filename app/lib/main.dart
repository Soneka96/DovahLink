import 'package:flutter/material.dart';

import 'app/app.dart';
import 'app/composition_root.dart';

/// Starts the DovahLink desktop client.
void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(DovahLinkApp(store: const AppCompositionRoot().createStore()));
}
