import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_countdown.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_renotify_button.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_section.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Registers pairing dependencies.
void initPairingDependencies() {
  sl.registerLazySingleton<IPairingMiddleware>(PairingMiddleware.new);
  sl.registerLazySingleton<IPairingRemoteDataSource>(
    () => PairingRemoteDataSource(sl<DovahLinkClient>()),
  );
  sl.registerFactoryParam<PairingCancelButtonViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return PairingCancelButtonViewModel.fromStore(store);
  });
  sl.registerFactoryParam<PairingCountdownViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return PairingCountdownViewModel.fromStore(store);
  });
  sl.registerFactoryParam<PairingDialogViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return PairingDialogViewModel.fromStore(store);
  });
  sl.registerFactoryParam<PairingSectionViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return PairingSectionViewModel.fromStore(store);
  });
  sl.registerFactoryParam<
    PairingRenotifyButtonViewModel,
    Store<AppState>,
    void
  >((Store<AppState> store, void _) {
    return PairingRenotifyButtonViewModel.fromStore(store);
  });
}
