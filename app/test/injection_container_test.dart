import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/bridge_list_screen.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connection_status_screen.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
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
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';

void main() {
  group('injection_container — shared registrations', () {
    test('initDependencies registers the connection ViewModel factory', () {
      initDependencies();

      expect(sl.isRegistered<ConnectionStatusScreenViewModel>(), isTrue);
    });

    test('initDependencies registers the router', () {
      initDependencies();

      expect(sl.isRegistered<GoRouter>(), isTrue);
    });

    test('initDependencies registers the navigator service', () {
      initDependencies();

      expect(sl.isRegistered<NavigatorService>(), isTrue);
    });

    test('the registered GoRouter is a true singleton', () {
      initDependencies();

      expect(identical(sl<GoRouter>(), sl<GoRouter>()), isTrue);
    });

    test('calling initDependencies twice does not throw or re-register', () {
      initDependencies();

      expect(initDependencies, returnsNormally);
      expect(sl.isRegistered<GoRouter>(), isTrue);
      expect(sl.isRegistered<NavigatorService>(), isTrue);
    });
  });

  group('injection_container — connection registrations', () {
    test('initDependencies registers the Bridge-list ViewModel factory', () {
      initDependencies();

      expect(sl.isRegistered<BridgeListScreenViewModel>(), isTrue);
    });
  });

  group('injection_container — pairing registrations', () {
    test('initDependencies registers the SDK client', () {
      initDependencies();

      expect(sl.isRegistered<DovahLinkClient>(), isTrue);
    });

    test('initDependencies registers the pairing remote data source', () {
      initDependencies();

      expect(sl.isRegistered<PairingRemoteDataSource>(), isTrue);
    });

    test('initDependencies registers the pairing repository', () {
      initDependencies();

      expect(sl.isRegistered<IPairingRepository>(), isTrue);
    });

    test('initDependencies registers the pairing use cases', () {
      initDependencies();

      expect(sl.isRegistered<AuthenticateUseCase>(), isTrue);
      expect(sl.isRegistered<RequestPairingUseCase>(), isTrue);
      expect(sl.isRegistered<ConfirmPairingCodeUseCase>(), isTrue);
      expect(sl.isRegistered<DisconnectUseCase>(), isTrue);
      expect(sl.isRegistered<RequestPairingRenotifyUseCase>(), isTrue);
      expect(sl.isRegistered<CancelPairingUseCase>(), isTrue);
      expect(sl.isRegistered<ObserveConnectionStatusUseCase>(), isTrue);
    });

    test('initDependencies registers the pairing ViewModel factory', () {
      initDependencies();

      expect(sl.isRegistered<PairingScreenViewModel>(), isTrue);
    });

    test('RequestPairingRenotifyUseCase is a singleton', () {
      initDependencies();

      expect(
        identical(
          sl<RequestPairingRenotifyUseCase>(),
          sl<RequestPairingRenotifyUseCase>(),
        ),
        isTrue,
      );
    });

    test('CancelPairingUseCase is a singleton', () {
      initDependencies();

      expect(
        identical(sl<CancelPairingUseCase>(), sl<CancelPairingUseCase>()),
        isTrue,
      );
    });

    test('RequestPairingRenotifyUseCase can be instantiated', () {
      initDependencies();

      expect(sl<RequestPairingRenotifyUseCase>(), isNotNull);
      expect(
        sl<RequestPairingRenotifyUseCase>(),
        isA<RequestPairingRenotifyUseCase>(),
      );
    });

    test('CancelPairingUseCase can be instantiated', () {
      initDependencies();

      expect(sl<CancelPairingUseCase>(), isNotNull);
      expect(sl<CancelPairingUseCase>(), isA<CancelPairingUseCase>());
    });
  });
}
