import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connections_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Registers connection presentation dependencies.
void initConnectionDependencies() {
  sl.registerFactoryParam<ConnectionsScreenViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return ConnectionsScreenViewModel.fromStore(store);
  });
}
