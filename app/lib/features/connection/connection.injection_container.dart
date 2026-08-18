import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/bridge_list_screen.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connection_status_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Registers connection presentation dependencies.
void initConnectionDependencies() {
  sl.registerFactoryParam<
    ConnectionStatusScreenViewModel,
    Store<AppState>,
    void
  >((Store<AppState> store, void _) {
    return ConnectionStatusScreenViewModel.fromStore(store);
  });
  sl.registerFactoryParam<BridgeListScreenViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return BridgeListScreenViewModel.fromStore(store);
  });
}
