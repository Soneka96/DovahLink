import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/session/presentation/state/session_shell.middleware.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_overview.viewmodel.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_shell.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Registers Session Shell dependencies.
void initSessionDependencies() {
  sl.registerLazySingleton<ISessionShellMiddleware>(
    () => SessionShellMiddleware(sl<NavigatorService>()),
  );
  sl.registerFactoryParam<SessionShellViewModel, Store<AppState>, String>((
    Store<AppState> store,
    String hostId,
  ) {
    return SessionShellViewModel.fromStore(store, hostId: hostId);
  });
  sl.registerFactoryParam<SessionOverviewViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return SessionOverviewViewModel.fromStore(store);
  });
}
