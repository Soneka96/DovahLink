import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/data/repositories/pairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/cancel_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/observe_connection_status.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing_renotify.usecase.dart';
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
  sl.registerLazySingleton<DovahLinkClient>(
    () => DovahLinkClient(storage: sl<IClientStorage>()),
  );
  sl.registerLazySingleton<IPairingRemoteDataSource>(
    () => PairingRemoteDataSource(sl<DovahLinkClient>()),
  );
  sl.registerLazySingleton<IPairingRepository>(
    () => PairingRepository(sl<IPairingRemoteDataSource>()),
  );
  sl.registerLazySingleton<AuthenticateUseCase>(
    () => AuthenticateUseCase(sl<IPairingRepository>()),
  );
  sl.registerLazySingleton<RequestPairingUseCase>(
    () => RequestPairingUseCase(sl<IPairingRepository>()),
  );
  sl.registerLazySingleton<ConfirmPairingCodeUseCase>(
    () => ConfirmPairingCodeUseCase(sl<IPairingRepository>()),
  );
  sl.registerLazySingleton<DisconnectUseCase>(
    () => DisconnectUseCase(sl<IPairingRepository>()),
  );
  sl.registerLazySingleton<RequestPairingRenotifyUseCase>(
    () => RequestPairingRenotifyUseCase(sl<IPairingRepository>()),
  );
  sl.registerLazySingleton<CancelPairingUseCase>(
    () => CancelPairingUseCase(sl<IPairingRepository>()),
  );
  sl.registerLazySingleton<ObserveConnectionStatusUseCase>(
    () => ObserveConnectionStatusUseCase(sl<IPairingRepository>()),
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
