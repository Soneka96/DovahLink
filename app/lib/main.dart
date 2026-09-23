import 'package:flutter/material.dart';

import 'package:dovahlink_client/app/app.dart';
import 'package:dovahlink_client/app/composition_root.dart';
import 'package:dovahlink_client/injection_container.dart';

/// Starts the DovahLink desktop client. Awaits dependency setup and store creation -- which
/// loads the persisted theme preset -- before the first frame, so the app never flashes the
/// default theme before the saved one.
Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initDependencies();
  final store = await const AppCompositionRoot().createStore();
  runApp(DovahLinkApp(store: store));
}
