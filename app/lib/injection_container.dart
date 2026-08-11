import 'package:get_it/get_it.dart';

import 'package:dovahlink_client/features/connection/connection.injection_container.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connection_status_screen.viewmodel.dart';

/// Global service locator used by registered application dependencies.
final GetIt sl = GetIt.instance;

/// Registers shared and feature dependencies once before [runApp].
void initDependencies() {
  if (sl.isRegistered<ConnectionStatusScreenViewModel>()) {
    return;
  }
  initConnectionDependencies();
}
