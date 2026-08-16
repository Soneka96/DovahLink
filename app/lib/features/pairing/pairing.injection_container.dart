import 'package:dovahlink_client_sdk/dovahlink_client.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/data/repositories/pairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/injection_container.dart';

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
}
