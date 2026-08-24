import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/data/repositories/pairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/cancel_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/observe_connection_status.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing_renotify.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Registers pairing dependencies.
void initPairingDependencies() {
  sl.registerLazySingleton<DovahLinkClient>(DovahLinkClient.windows);
  sl.registerLazySingleton<PairingRemoteDataSource>(
    () => PairingRemoteDataSourceImpl(sl<DovahLinkClient>()),
  );
  sl.registerLazySingleton<IPairingRepository>(
    () => PairingRepositoryImpl(sl<PairingRemoteDataSource>()),
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
  sl.registerFactoryParam<PairingScreenViewModel, Store<AppState>, void>((
    Store<AppState> store,
    void _,
  ) {
    return PairingScreenViewModel.fromStore(store);
  });
}
