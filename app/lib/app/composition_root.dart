import 'package:fpdart/fpdart.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/domain/usecases/load_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.middleware.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/load_device_name.usecase.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.middleware.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show IClientStorage, UnsupportedClientStorage;

/// Composes application-wide dependencies before Flutter starts.
///
/// Each feature's middleware is added to the list here as that feature is
/// built.
class AppCompositionRoot {
  /// Creates a composition root without retaining mutable global state.
  const AppCompositionRoot();

  /// Builds the Redux store used by [DovahLinkApp]. Loads the persisted theme and resolved device
  /// name before creating the store, so the first frame reflects local preferences. An unavailable
  /// device-name override remains `null` and is not supplied during pairing, preventing an
  /// uncertain fallback from replacing the Host's existing label.
  Future<Store<AppState>> createStore() async {
    final Either<Failure, DovahThemePreset> loaded =
        await sl<LoadThemePresetUseCase>()(NoParams());
    final DovahThemePreset preset = loaded.fold(
      (Failure _) => defaultThemePreset,
      (DovahThemePreset preset) => preset,
    );
    final Either<Failure, String> loadedDeviceName =
        await sl<ILoadDeviceNameUseCase>()(NoParams());
    final DeviceIdentityState deviceIdentity = loadedDeviceName.fold(
      (Failure failure) =>
          DeviceIdentityState(displayName: null, loadFailure: failure.message),
      (String name) => DeviceIdentityState(displayName: name),
    );
    final IConnectionMiddleware connectionMiddleware =
        sl<IConnectionMiddleware>();
    final Store<AppState> store = const CreateStore()(
      middleware: [
        connectionMiddleware.call,
        sl<IPairingMiddleware>().call,
        sl<ISessionShellMiddleware>().call,
        AppearanceMiddleware().call,
        sl<IDeviceIdentityMiddleware>().call,
      ],
      initialState: AppState.initial(
        appearance: AppearanceState(activePreset: preset),
        deviceIdentity: deviceIdentity,
        pairingSupport: sl<IClientStorage>() is UnsupportedClientStorage
            ? PairingSupport.secureStorageUnavailable
            : PairingSupport.available,
      ),
    );
    connectionMiddleware.initialize(store);
    return store;
  }
}
