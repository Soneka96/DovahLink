import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.middleware.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

/// Composes application-wide dependencies before Flutter starts.
///
/// Each feature's middleware is added to the list here as that feature is
/// built.
class AppCompositionRoot {
  /// Creates a composition root without retaining mutable global state.
  const AppCompositionRoot();

  /// Builds the Redux store used by [DovahLinkApp].
  Store<AppState> createStore() => const CreateStore()(
    middleware: [ConnectionMiddleware().call, PairingMiddleware().call],
  );
}
