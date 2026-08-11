import 'package:redux/redux.dart';

import '../shared/state/app_state.dart';
import '../shared/state/create_store.dart';

/// Composes application-wide dependencies before Flutter starts.
class AppCompositionRoot {
  /// Creates a composition root without retaining mutable global state.
  const AppCompositionRoot();

  /// Builds the Redux store used by [DovahLinkApp].
  Store<AppState> createStore() => CreateStore()();
}
