import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/host_list_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Registers connection presentation dependencies.
void initConnectionDependencies() {
  sl.registerFactoryParam<HostListScreenViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return HostListScreenViewModel.fromStore(store);
  });
}
