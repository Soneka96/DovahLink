import 'package:fpdart/fpdart.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/domain/usecases/load_theme_preset.usecase.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.middleware.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
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

  /// Builds the Redux store used by [DovahLinkApp]. Loads the persisted theme preset first (a
  /// missing or unrecognized value already resolves to the default within that use case, so a
  /// [Failure] here means persistence itself was unavailable, not an invalid stored value) so the
  /// store's very first state already reflects it -- avoiding a default-theme-then-saved-theme
  /// flash after `runApp` -- rather than dispatching a load action once the app is already
  /// running.
  Future<Store<AppState>> createStore() async {
    final Either<Failure, DovahThemePreset> loaded =
        await sl<LoadThemePresetUseCase>()(NoParams());
    final DovahThemePreset preset = loaded.fold(
      (Failure _) => defaultThemePreset,
      (DovahThemePreset preset) => preset,
    );
    return const CreateStore()(
      middleware: [sl<IPairingMiddleware>().call, AppearanceMiddleware().call],
      initialState: AppState.initial(
        appearance: AppearanceState(activePreset: preset),
        pairingSupport: sl<IClientStorage>() is UnsupportedClientStorage
            ? PairingSupport.secureStorageUnavailable
            : PairingSupport.available,
      ),
    );
  }
}
